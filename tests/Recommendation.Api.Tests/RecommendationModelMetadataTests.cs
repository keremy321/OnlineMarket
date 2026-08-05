using Microsoft.EntityFrameworkCore;
using Recommendation.Api.Domain.Entities;
using Recommendation.Api.Infrastructure.Persistence;

namespace Recommendation.Api.Tests;

public sealed class RecommendationModelMetadataTests
{
    [Fact]
    public void Product_description_model_matches_the_migration_contract()
    {
        var options = new DbContextOptionsBuilder<RecommendationDbContext>()
            .UseSqlServer(
                "Server=127.0.0.1;Database=ModelMetadataOnly;User Id=unused;Password=unused;TrustServerCertificate=True")
            .Options;
        using var context = new RecommendationDbContext(options);

        var product = context.Model.FindEntityType(typeof(ProductSnapshot));
        var description = product!.FindProperty(
            nameof(ProductSnapshot.Description));

        Assert.NotNull(description);
        Assert.True(description.IsNullable);
        Assert.Equal(2000, description.GetMaxLength());
        Assert.Equal("nvarchar(2000)", description.GetColumnType());
    }
}
