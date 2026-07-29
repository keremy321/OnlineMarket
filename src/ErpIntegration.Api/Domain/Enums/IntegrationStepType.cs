namespace ErpIntegration.Api.Domain.Enums;

public enum IntegrationStepType : byte
{
    EnsureCustomer = 1,
    CreateOrder = 2,
    CreateStockMovement = 3,
    CreateAccountingEntry = 4
}
