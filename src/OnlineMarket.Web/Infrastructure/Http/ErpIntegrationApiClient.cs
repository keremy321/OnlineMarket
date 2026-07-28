using System.Net.Http.Json;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;

namespace OnlineMarket.Web.Infrastructure.Http;

public class ErpIntegrationApiClient : IErpIntegrationClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ErpIntegrationApiClient> _logger;

    private static DateTime _offlineUntilUtc = DateTime.MinValue;
    private static readonly object _lock = new();

    public ErpIntegrationApiClient(HttpClient httpClient, ILogger<ErpIntegrationApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    private bool IsCircuitOpen()
    {
        lock (_lock)
        {
            return DateTime.UtcNow < _offlineUntilUtc;
        }
    }

    private void RecordFailure()
    {
        lock (_lock)
        {
            _offlineUntilUtc = DateTime.UtcNow.AddSeconds(15);
        }
    }

    public async Task<ErpOrderTransferStatusDto?> GetErpOrderTransferStatusAsync(Guid orderId)
    {
        if (IsCircuitOpen())
        {
            return new ErpOrderTransferStatusDto(orderId, "Pending", "NotStarted", null, "ERP Entegrasyon Servisine ulaşılamadı (Circuit open).");
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var response = await _httpClient.GetFromJsonAsync<ErpOrderTransferStatusDto>($"/api/v1/integration/orders/{orderId}", cts.Token);
            return response;
        }
        catch (Exception ex)
        {
            RecordFailure();
            _logger.LogWarning("ErpIntegration.Api unavailable ({Message}). Circuit opened for 15s.", ex.Message);
            return new ErpOrderTransferStatusDto(orderId, "Pending", "NotStarted", null, "ERP Entegrasyon Servisine ulaşılamadı.");
        }
    }
}
