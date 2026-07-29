using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OnlineMarket.Web.Application.Interfaces;

namespace OnlineMarket.Web.Infrastructure.Persistence;

public sealed class SqlServerOrderNumberGenerator : IOrderNumberGenerator
{
    private readonly OnlineMarketDbContext _dbContext;

    public SqlServerOrderNumberGenerator(OnlineMarketDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string> NextAsync(CancellationToken cancellationToken = default)
    {
        var currentTransaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Order number allocation requires an active database transaction.");

        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = currentTransaction.GetDbTransaction();
        command.CommandType = CommandType.Text;
        command.CommandText = "SELECT NEXT VALUE FOR [dbo].[OnlineMarketOrderNumberSequence];";

        var value = await command.ExecuteScalarAsync(cancellationToken);
        var sequenceValue = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);

        return $"ORD-{sequenceValue:D20}";
    }
}
