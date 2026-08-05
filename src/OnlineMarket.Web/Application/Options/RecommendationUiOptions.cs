namespace OnlineMarket.Web.Application.Options;

public sealed class RecommendationUiOptions
{
    public const string SectionName = "Recommendations:Ui";
    public const int MaximumPersonalizedDisplayLimit = 8;

    public int PersonalizedDisplayLimit { get; set; } =
        MaximumPersonalizedDisplayLimit;
}
