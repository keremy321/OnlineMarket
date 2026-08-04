using ErpIntegration.Api.Application.Interfaces;
using ErpIntegration.Api.Application.Models;
using ErpIntegration.Api.Contracts;
using ErpIntegration.Api.Domain.Entities;
using ErpIntegration.Api.Domain.Enums;

namespace ErpIntegration.Api.Application.Services;

public sealed class IntegrationOrderService(
    IIntegrationOrderStore store,
    OrderReadyForErpV1Validator validator,
    TimeProvider timeProvider,
    ILogger<IntegrationOrderService> logger)
    : IIntegrationOrderService
{
    private const int ManualRetryAttemptAllowance = 5;

    public async Task<IntakeOrderResult> AcceptAsync(
        OrderReadyForErpV1Request request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = validator.Validate(request);
        if (!validation.IsValid)
        {
            return new IntakeOrderResult(
                null,
                null,
                validation.Errors);
        }

        var normalized = validation.NormalizedRequest;
        var payloadHash = CanonicalPayloadHasher.Compute(normalized);
        var receivedAtUtc = GetUtcNow();
        var batch = CreateBatch(normalized, payloadHash, receivedAtUtc);
        var persistenceResult = await store.AcceptAsync(
            batch,
            cancellationToken);

        switch (persistenceResult.Outcome)
        {
            case IntakeStoreOutcome.Created:
                logger.LogInformation(
                    "Accepted ERP integration event {EventId} for order {OrderId} as batch {BatchId}.",
                    normalized.EventId,
                    normalized.OrderId,
                    batch.Id);
                return new IntakeOrderResult(
                    persistenceResult.Accepted,
                    null,
                    null);

            case IntakeStoreOutcome.Replay:
                logger.LogInformation(
                    "Accepted idempotent replay of ERP integration event {EventId} for order {OrderId}.",
                    normalized.EventId,
                    normalized.OrderId);
                return new IntakeOrderResult(
                    persistenceResult.Accepted,
                    null,
                    null);

            case IntakeStoreOutcome.PayloadConflict:
                logger.LogWarning(
                    "Rejected ERP integration event {EventId} because its canonical payload differs from the accepted payload.",
                    normalized.EventId);
                return new IntakeOrderResult(
                    null,
                    new ApplicationError(
                        "Idempotency.PayloadConflict",
                        "The EventId has already been accepted with a different payload.",
                        false),
                    null);

            case IntakeStoreOutcome.OrderConflict:
                logger.LogWarning(
                    "Rejected ERP integration event {EventId} because order {OrderId} or another unique order identity was already accepted.",
                    normalized.EventId,
                    normalized.OrderId);
                return new IntakeOrderResult(
                    null,
                    new ApplicationError(
                        "Integration.OrderAlreadyAccepted",
                        "The order or one of its unique integration identities has already been accepted.",
                        false),
                    null);

            default:
                throw new InvalidOperationException(
                    $"Unknown intake persistence outcome '{persistenceResult.Outcome}'.");
        }
    }

    public async Task<IntegrationOrderStatusResponse?> GetOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty)
        {
            return null;
        }

        var state = await store.GetOrderAsync(orderId, cancellationToken);
        return state is null ? null : MapOrder(state);
    }

    public async Task<RetryOrderResult> RetryAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty)
        {
            return NotFoundRetryResult();
        }

        var batch = await store.GetForRetryAsync(orderId, cancellationToken);
        if (batch is null)
        {
            return NotFoundRetryResult();
        }

        if (batch.Status != IntegrationBatchStatus.WaitingManualRetry)
        {
            return RetryNotAllowed();
        }

        var candidate = batch.Steps
            .OrderBy(step => step.SequenceNumber)
            .FirstOrDefault(step =>
                step.Status == IntegrationStepStatus.WaitingManualRetry);
        if (candidate is null
            || batch.Steps.Any(step =>
                step.SequenceNumber < candidate.SequenceNumber
                && step.Status != IntegrationStepStatus.Succeeded))
        {
            return RetryNotAllowed();
        }

        candidate.Status = IntegrationStepStatus.Pending;
        candidate.MaxAttempts = candidate.MaxAttempts
            > int.MaxValue - ManualRetryAttemptAllowance
                ? int.MaxValue
                : Math.Max(candidate.MaxAttempts, candidate.AttemptCount)
                    + ManualRetryAttemptAllowance;
        candidate.NextAttemptAtUtc = null;
        candidate.LockedAtUtc = null;
        candidate.LockedBy = null;
        candidate.StartedAtUtc = null;
        candidate.CompletedAtUtc = null;
        candidate.ExternalReference = null;
        candidate.LastHttpStatusCode = null;
        candidate.LastErrorType = null;
        candidate.LastErrorCode = null;
        candidate.LastErrorMessage = null;

        var hasSucceededSteps = batch.Steps.Any(step =>
            step.Status == IntegrationStepStatus.Succeeded);
        batch.Status = hasSucceededSteps
            ? IntegrationBatchStatus.PartiallySucceeded
            : IntegrationBatchStatus.Pending;
        batch.CurrentStepType = candidate.StepType;
        batch.CompletedAtUtc = null;
        batch.LastErrorCode = null;
        batch.LastErrorMessage = null;

        if (!await store.SaveRetryAsync(cancellationToken))
        {
            return new RetryOrderResult(
                null,
                new ApplicationError(
                    "Integration.ConcurrentUpdate",
                    "The integration job changed while the retry was being scheduled.",
                    true));
        }

        logger.LogInformation(
            "Scheduled manual retry for ERP integration order {OrderId} at step {StepType}; the existing idempotency key was retained.",
            orderId,
            candidate.StepType);

        var response = await GetOrderAsync(orderId, cancellationToken)
            ?? throw new InvalidOperationException(
                "The integration order disappeared after its retry was saved.");
        return new RetryOrderResult(response, null);
    }

    public async Task<PagedResponse<IntegrationJobSummaryResponse>>
        GetCustomerOrdersAsync(
            Guid customerId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
    {
        ValidatePaging(page, pageSize);
        var result = await store.GetCustomerOrdersAsync(
            customerId,
            checked((page - 1) * pageSize),
            pageSize,
            cancellationToken);
        return MapPage(result, page, pageSize);
    }

    public async Task<PagedResponse<IntegrationJobSummaryResponse>>
        GetJobsAsync(
            IntegrationBatchStatus? status,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
    {
        ValidatePaging(page, pageSize);
        var result = await store.GetJobsAsync(
            status,
            checked((page - 1) * pageSize),
            pageSize,
            cancellationToken);
        return MapPage(result, page, pageSize);
    }

    private static IntegrationBatch CreateBatch(
        OrderReadyForErpV1Request request,
        string payloadHash,
        DateTime receivedAtUtc)
    {
        var customer = request.Customer!;
        var address = request.Address!;
        var totals = request.Totals!;
        var batchId = Guid.NewGuid();
        var processedEvent = new ProcessedEvent
        {
            EventId = request.EventId,
            EventType = "OrderReadyForErpV1",
            PayloadHash = payloadHash,
            CorrelationId = request.CorrelationId,
            ReceivedAtUtc = receivedAtUtc,
            ProcessedAtUtc = receivedAtUtc
        };
        var batch = new IntegrationBatch
        {
            Id = batchId,
            EventId = request.EventId,
            MarketOrderId = request.OrderId,
            OrderNumber = request.OrderNumber!,
            CustomerId = customer.CustomerId,
            Status = IntegrationBatchStatus.Pending,
            CurrentStepType = IntegrationStepType.EnsureCustomer,
            CorrelationId = request.CorrelationId,
            CreatedAtUtc = receivedAtUtc,
            ProcessedEvent = processedEvent
        };
        processedEvent.Batch = batch;

        batch.OrderSnapshot = new IntegrationOrderSnapshot
        {
            BatchId = batchId,
            CustomerId = customer.CustomerId,
            FirstName = customer.FirstName!,
            LastName = customer.LastName!,
            RecipientName = address.RecipientName!,
            Email = customer.Email!,
            PhoneNumber = address.PhoneNumber!,
            AddressLine1 = address.AddressLine1!,
            AddressLine2 = address.AddressLine2,
            District = address.District!,
            City = address.City!,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode!,
            OrderPlacedAtUtc = request.OrderPlacedAtUtc,
            PaymentMethod = request.PaymentMethod,
            Subtotal = totals.Subtotal,
            VatTotal = totals.VatTotal,
            GrandTotal = totals.GrandTotal,
            Currency = totals.Currency!,
            Batch = batch
        };

        foreach (var itemValue in request.Items!)
        {
            var item = itemValue!;
            batch.OrderLines.Add(new IntegrationOrderLine
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                ProductId = item.ProductId,
                Sku = item.Sku!,
                ProductName = item.ProductName!,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                VatRate = item.VatRate,
                NetLineAmount = item.NetLineAmount,
                VatAmount = item.VatAmount,
                LineTotal = item.LineTotal,
                Batch = batch
            });
        }

        AddStep(
            batch,
            IntegrationStepType.EnsureCustomer,
            1,
            $"customer:{request.OrderId}");
        AddStep(
            batch,
            IntegrationStepType.CreateOrder,
            2,
            $"order:{request.OrderId}");
        AddStep(
            batch,
            IntegrationStepType.CreateStockMovement,
            3,
            $"stock:{request.OrderId}");
        AddStep(
            batch,
            IntegrationStepType.CreateAccountingEntry,
            4,
            $"accounting:{request.OrderId}");

        return batch;
    }

    private static void AddStep(
        IntegrationBatch batch,
        IntegrationStepType stepType,
        byte sequenceNumber,
        string idempotencyKey)
    {
        batch.Steps.Add(new IntegrationStep
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            StepType = stepType,
            SequenceNumber = sequenceNumber,
            Status = IntegrationStepStatus.Pending,
            IdempotencyKey = idempotencyKey,
            AttemptCount = 0,
            MaxAttempts = 5,
            Batch = batch
        });
    }

    private static IntegrationOrderStatusResponse MapOrder(
        IntegrationOrderReadState state)
    {
        return new IntegrationOrderStatusResponse(
            state.OrderId,
            GetPublicStatus(state.Status),
            state.CurrentStepType?.ToString(),
            AsUtc(state.LastAttemptAtUtc),
            state.LastErrorMessage,
            state.BatchId,
            state.EventId,
            state.OrderNumber,
            state.CustomerId,
            state.CorrelationId,
            state.Status.ToString(),
            AsUtc(state.CreatedAtUtc),
            AsUtc(state.StartedAtUtc),
            AsUtc(state.CompletedAtUtc),
            state.LastErrorCode,
            state.Steps
                .OrderBy(step => step.SequenceNumber)
                .Select(step => new IntegrationStepStatusResponse(
                    step.StepType.ToString(),
                    step.SequenceNumber,
                    step.Status.ToString(),
                    step.AttemptCount,
                    step.MaxAttempts,
                    AsUtc(step.NextAttemptAtUtc),
                    AsUtc(step.LastAttemptAtUtc),
                    AsUtc(step.CompletedAtUtc),
                    step.ExternalReference,
                    step.LastErrorCode,
                    step.LastErrorMessage))
                .ToArray());
    }

    private static PagedResponse<IntegrationJobSummaryResponse> MapPage(
        PagedReadResult<IntegrationJobReadState> pageResult,
        int page,
        int pageSize)
    {
        var items = pageResult.Items
            .Select(state => new IntegrationJobSummaryResponse(
                state.BatchId,
                state.EventId,
                state.OrderId,
                state.OrderNumber,
                state.CustomerId,
                state.CorrelationId,
                GetPublicStatus(state.Status),
                state.Status.ToString(),
                state.CurrentStepType?.ToString(),
                AsUtc(state.CreatedAtUtc),
                AsUtc(state.LastAttemptAtUtc),
                state.LastErrorCode,
                state.LastErrorMessage))
            .ToArray();
        return new PagedResponse<IntegrationJobSummaryResponse>(
            items,
            page,
            pageSize,
            pageResult.TotalCount);
    }

    private static string GetPublicStatus(IntegrationBatchStatus status)
    {
        return status switch
        {
            IntegrationBatchStatus.Pending => "Pending",
            IntegrationBatchStatus.InProgress
                or IntegrationBatchStatus.PartiallySucceeded => "Processing",
            IntegrationBatchStatus.Succeeded => "Completed",
            IntegrationBatchStatus.WaitingManualRetry
                or IntegrationBatchStatus.FailedPermanent => "Failed",
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unknown integration batch status.")
        };
    }

    private DateTime GetUtcNow()
    {
        var value = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(
            value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond,
            DateTimeKind.Utc);
    }

    private static DateTime AsUtc(DateTime value)
    {
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private static DateTime? AsUtc(DateTime? value)
    {
        return value.HasValue ? AsUtc(value.Value) : null;
    }

    private static void ValidatePaging(int page, int pageSize)
    {
        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                "Page must be at least one.");
        }

        if (pageSize is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                "PageSize must be between one and 100.");
        }
    }

    private static RetryOrderResult NotFoundRetryResult()
    {
        return new RetryOrderResult(
            null,
            new ApplicationError(
                "Integration.OrderNotFound",
                "The integration order was not found.",
                false));
    }

    private static RetryOrderResult RetryNotAllowed()
    {
        return new RetryOrderResult(
            null,
            new ApplicationError(
                "Integration.ManualRetryNotAllowed",
                "Only a failed step waiting for manual recovery may be retried.",
                false));
    }
}
