using System.Collections.Concurrent;
using System.Net;
using System.Text;
using ErpIntegration.Api.Application.Models;
using ErpIntegration.Api.Domain.Enums;
using ErpIntegration.Api.Infrastructure.Configuration;
using ErpIntegration.Api.Infrastructure.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ErpIntegration.Api.Tests;

public sealed class MockErpClientTests
{
    [Fact]
    public async Task Fast_retry_reuses_headers_and_payload()
    {
        var handler = new QueueHttpMessageHandler(
            Response(
                HttpStatusCode.ServiceUnavailable,
                """{"code":"MockErp.Unavailable","message":"Unavailable","retryable":true}"""),
            Response(
                HttpStatusCode.Created,
                """{"erpCustomerId":"11111111-1111-1111-1111-111111111111","erpCustomerCode":"CARI-TEST","externalCustomerId":"22222222-2222-2222-2222-222222222222","created":true}"""));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/"),
            Timeout = TimeSpan.FromSeconds(2)
        };
        var client = CreateClient(httpClient, fastRetryCount: 1);
        var claim = CreateClaim();

        var result = await client.ExecuteAsync(claim);

        Assert.True(result.Succeeded);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(
            handler.Requests[0].Body,
            handler.Requests[1].Body);
        Assert.All(
            handler.Requests,
            request =>
            {
                Assert.Equal("test-api-key", request.ApiKey);
                Assert.Equal(
                    claim.IdempotencyKey,
                    request.IdempotencyKey);
                Assert.Equal(
                    claim.CorrelationId.ToString("D"),
                    request.CorrelationId);
            });
    }

    [Fact]
    public async Task Validation_failure_is_permanent_and_not_retried()
    {
        var handler = new QueueHttpMessageHandler(
            Response(
                HttpStatusCode.BadRequest,
                """{"code":"Validation.Failed","message":"Invalid","retryable":false}"""));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/"),
            Timeout = TimeSpan.FromSeconds(2)
        };
        var client = CreateClient(httpClient, fastRetryCount: 2);

        var result = await client.ExecuteAsync(CreateClaim());

        Assert.Equal(
            IntegrationResultType.PermanentFailure,
            result.ResultType);
        Assert.Equal("Validation.Failed", result.ErrorCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Authentication_error_is_permanent_even_with_503_status()
    {
        var handler = new QueueHttpMessageHandler(
            Response(
                HttpStatusCode.ServiceUnavailable,
                """{"code":"Authentication.ConfigurationMissing","message":"Unavailable","retryable":true}"""));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/"),
            Timeout = TimeSpan.FromSeconds(2)
        };
        var client = CreateClient(httpClient, fastRetryCount: 2);

        var result = await client.ExecuteAsync(CreateClaim());

        Assert.Equal(
            IntegrationResultType.PermanentFailure,
            result.ResultType);
        Assert.Equal(
            "Authentication.ConfigurationMissing",
            result.ErrorCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void Configuration_validator_requires_endpoint_and_api_key()
    {
        var result = new MockErpClientOptionsValidator().Validate(
            null,
            new MockErpClientOptions
            {
                BaseAddress = "not-a-uri",
                ApiKey = string.Empty
            });

        Assert.False(result.Succeeded);
        var failures = Assert.IsAssignableFrom<IEnumerable<string>>(
            result.Failures);
        Assert.Contains(
            failures,
            failure => failure.Contains(
                "BaseAddress",
                StringComparison.Ordinal));
        Assert.Contains(
            failures,
            failure => failure.Contains(
                "ApiKey",
                StringComparison.Ordinal));

        var workerResult = new IntegrationWorkerOptionsValidator().Validate(
            null,
            new IntegrationWorkerOptions
            {
                PollInterval = TimeSpan.Zero,
                LockTimeout = TimeSpan.Zero,
                BaseRetryDelay = TimeSpan.Zero,
                MaxRetryDelay = TimeSpan.Zero
            });
        Assert.False(workerResult.Succeeded);
    }

    [Fact]
    public async Task Retry_after_beyond_fast_window_is_left_for_durable_retry()
    {
        var response = Response(
            HttpStatusCode.TooManyRequests,
            """{"code":"RateLimit.Exceeded","message":"Slow down","retryable":true}""");
        response.Headers.RetryAfter =
            new System.Net.Http.Headers.RetryConditionHeaderValue(
                TimeSpan.FromSeconds(30));
        var handler = new QueueHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/"),
            Timeout = TimeSpan.FromSeconds(2)
        };
        var client = CreateClient(
            httpClient,
            fastRetryCount: 2,
            maxFastRetryDelay: TimeSpan.FromSeconds(1));

        var result = await client.ExecuteAsync(CreateClaim());

        Assert.Equal(
            IntegrationResultType.TransientFailure,
            result.ResultType);
        Assert.Equal("RateLimit.Exceeded", result.ErrorCode);
        Assert.True(result.RetryAfterUtc > DateTime.UtcNow.AddSeconds(20));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Circuit_breaker_opens_after_repeated_transient_failures()
    {
        var handler = new QueueHttpMessageHandler(
            Response(
                HttpStatusCode.ServiceUnavailable,
                """{"code":"MockErp.Unavailable","message":"Unavailable","retryable":true}"""),
            Response(
                HttpStatusCode.ServiceUnavailable,
                """{"code":"MockErp.Unavailable","message":"Unavailable","retryable":true}"""));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/"),
            Timeout = TimeSpan.FromSeconds(2)
        };
        var client = CreateClient(
            httpClient,
            fastRetryCount: 0,
            circuitBreakerThreshold: 2);

        Assert.Equal(
            IntegrationResultType.TransientFailure,
            (await client.ExecuteAsync(CreateClaim())).ResultType);
        Assert.Equal(
            IntegrationResultType.TransientFailure,
            (await client.ExecuteAsync(CreateClaim())).ResultType);
        var circuitResult = await client.ExecuteAsync(CreateClaim());

        Assert.Equal("MockErp.CircuitOpen", circuitResult.ErrorCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    private static MockErpClient CreateClient(
        HttpClient httpClient,
        int fastRetryCount,
        TimeSpan? maxFastRetryDelay = null,
        int circuitBreakerThreshold = 5)
    {
        return new MockErpClient(
            httpClient,
            Options.Create(new MockErpClientOptions
            {
                BaseAddress = "http://localhost/",
                ApiKey = "test-api-key",
                Timeout = TimeSpan.FromSeconds(2),
                FastRetryCount = fastRetryCount,
                FastRetryDelay = TimeSpan.Zero,
                MaxFastRetryDelay =
                    maxFastRetryDelay ?? TimeSpan.Zero,
                CircuitBreakerFailureThreshold =
                    circuitBreakerThreshold,
                CircuitBreakerBreakDuration = TimeSpan.FromSeconds(30)
            }),
            new MockErpCircuitBreaker(),
            TimeProvider.System,
            NullLogger<MockErpClient>.Instance);
    }

    private static ClaimedIntegrationStep CreateClaim()
    {
        var customerId = Guid.Parse(
            "22222222-2222-2222-2222-222222222222");
        return new ClaimedIntegrationStep(
            Guid.NewGuid(),
            Guid.NewGuid(),
            IntegrationStepType.EnsureCustomer,
            1,
            1,
            5,
            $"customer:{Guid.NewGuid()}",
            Guid.NewGuid(),
            "ORD-TEST",
            customerId,
            Guid.NewGuid(),
            DateTime.UtcNow,
            new IntegrationWorkerOrderSnapshot(
                customerId,
                "Ada",
                "Lovelace",
                "Ada Lovelace",
                "ada@example.test",
                "+905550000000",
                "Test Street 1",
                null,
                "Kadikoy",
                "Istanbul",
                null,
                "TR",
                DateTime.UtcNow,
                PaymentMethod.CardSimulation,
                100m,
                20m,
                120m,
                "TRY"),
            [],
            null);
    }

    private static HttpResponseMessage Response(
        HttpStatusCode statusCode,
        string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                body,
                Encoding.UTF8,
                "application/json")
        };
    }

    private sealed class QueueHttpMessageHandler(
        params HttpResponseMessage[] responses)
        : HttpMessageHandler
    {
        private readonly ConcurrentQueue<HttpResponseMessage> responses =
            new(responses);

        public List<RecordedHttpRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedHttpRequest(
                request.Headers
                    .GetValues(MockErpClient.ApiKeyHeaderName)
                    .Single(),
                request.Headers
                    .GetValues(MockErpClient.IdempotencyHeaderName)
                    .Single(),
                request.Headers
                    .GetValues(MockErpClient.CorrelationHeaderName)
                    .Single(),
                await request.Content!.ReadAsStringAsync(
                    cancellationToken)));
            return responses.TryDequeue(out var response)
                ? response
                : throw new InvalidOperationException(
                    "No response was configured.");
        }
    }

    private sealed record RecordedHttpRequest(
        string ApiKey,
        string IdempotencyKey,
        string CorrelationId,
        string Body);
}
