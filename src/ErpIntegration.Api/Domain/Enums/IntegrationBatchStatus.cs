namespace ErpIntegration.Api.Domain.Enums;

public enum IntegrationBatchStatus : byte
{
    Pending = 1,
    InProgress = 2,
    PartiallySucceeded = 3,
    Succeeded = 4,
    WaitingManualRetry = 5,
    FailedPermanent = 6
}
