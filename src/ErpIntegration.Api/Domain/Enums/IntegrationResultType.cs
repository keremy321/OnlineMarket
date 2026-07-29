namespace ErpIntegration.Api.Domain.Enums;

public enum IntegrationResultType : byte
{
    Succeeded = 1,
    TransientFailure = 2,
    PermanentFailure = 3,
    IdempotentReplay = 4
}
