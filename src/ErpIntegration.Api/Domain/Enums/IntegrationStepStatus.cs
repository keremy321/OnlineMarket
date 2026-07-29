namespace ErpIntegration.Api.Domain.Enums;

public enum IntegrationStepStatus : byte
{
    Pending = 1,
    InProgress = 2,
    Retrying = 3,
    Succeeded = 4,
    FailedPermanent = 5,
    WaitingManualRetry = 6
}
