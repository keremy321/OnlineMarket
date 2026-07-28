namespace OnlineMarket.Web.Domain.Enums;

public enum UnitType : byte
{
    Piece = 1,
    Gram = 2,
    Kilogram = 3,
    Millilitre = 4,
    Litre = 5,
    Package = 6
}

public enum CartStatus : byte
{
    Active = 1,
    Converted = 2,
    Abandoned = 3
}

public enum OrderStatus : byte
{
    Confirmed = 1,
    Cancelled = 2
}

public enum PaymentMethod : byte
{
    CashSimulation = 1,
    CardSimulation = 2,
    TransferSimulation = 3
}

public enum PaymentStatus : byte
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4
}

public enum StockMovementType : byte
{
    Initial = 1,
    AdminIncrease = 2,
    AdminDecrease = 3,
    Sale = 4,
    Rollback = 5,
    Correction = 6
}

public enum StockReferenceType : byte
{
    None = 1,
    Order = 2,
    AdminOperation = 3,
    Seed = 4
}

public enum OutboxStatus : byte
{
    Pending = 1,
    Processing = 2,
    Processed = 3,
    Retrying = 4,
    FailedPermanent = 5
}
