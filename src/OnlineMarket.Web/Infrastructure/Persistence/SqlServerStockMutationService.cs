using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;

namespace OnlineMarket.Web.Infrastructure.Persistence;

public sealed class SqlServerStockMutationService : IStockMutationService
{
    private readonly OnlineMarketDbContext _dbContext;

    public SqlServerStockMutationService(OnlineMarketDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<StockMutationResultDto?> TryDecreaseAsync(
        Guid productId,
        int requestedQuantity,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (requestedQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedQuantity));
        }

        const string sql = """
            UPDATE [Stocks] WITH (ROWLOCK)
            SET [Quantity] = [Quantity] - @RequestedQuantity,
                [UpdatedAtUtc] = @UpdatedAtUtc
            OUTPUT DELETED.[Quantity], INSERTED.[Quantity]
            WHERE [ProductId] = @ProductId
              AND [Quantity] >= @RequestedQuantity;
            """;

        return ExecuteMutationAsync(
            sql,
            productId,
            updatedAtUtc,
            "@RequestedQuantity",
            requestedQuantity,
            cancellationToken);
    }

    public Task<StockMutationResultDto?> TryAdjustAsync(
        Guid productId,
        int quantityChange,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (quantityChange == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityChange));
        }

        const string sql = """
            UPDATE [Stocks] WITH (ROWLOCK)
            SET [Quantity] = [Quantity] + @QuantityChange,
                [UpdatedAtUtc] = @UpdatedAtUtc
            OUTPUT DELETED.[Quantity], INSERTED.[Quantity]
            WHERE [ProductId] = @ProductId
              AND CONVERT(bigint, [Quantity]) + @QuantityChange BETWEEN 0 AND 2147483647;
            """;

        return ExecuteMutationAsync(
            sql,
            productId,
            updatedAtUtc,
            "@QuantityChange",
            quantityChange,
            cancellationToken);
    }

    private async Task<StockMutationResultDto?> ExecuteMutationAsync(
        string sql,
        Guid productId,
        DateTime updatedAtUtc,
        string quantityParameterName,
        int quantityValue,
        CancellationToken cancellationToken)
    {
        var currentTransaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Stock mutations require an active database transaction.");

        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = currentTransaction.GetDbTransaction();
        command.CommandText = sql;

        AddParameter(command, "@ProductId", DbType.Guid, productId);
        AddParameter(command, quantityParameterName, DbType.Int32, quantityValue);
        AddParameter(command, "@UpdatedAtUtc", DbType.DateTime2, updatedAtUtc);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StockMutationResultDto(reader.GetInt32(0), reader.GetInt32(1));
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        DbType type,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
