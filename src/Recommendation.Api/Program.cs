using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Options;
using Recommendation.Api.Application.Services;
using Recommendation.Api.Contracts;
using Recommendation.Api.Infrastructure.Http;
using Recommendation.Api.Infrastructure.Persistence;
using Recommendation.Api.Infrastructure.Security;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<RecommendationDbContext>((services, options) =>
{
    var connectionString = services
        .GetRequiredService<IConfiguration>()
        .GetConnectionString("RecommendationDb")
        ?? throw new InvalidOperationException(
            "Connection string 'RecommendationDb' is required.");
    options.UseSqlServer(connectionString);
});
builder.Services.AddScoped<
    IRecommendationEventStore,
    SqlServerRecommendationEventStore>();
builder.Services.AddScoped<
    IRecommendationEventIngestionService,
    RecommendationEventIngestionService>();
builder.Services.AddSingleton<
    IRecommendationSubjectIdDeriver,
    HmacRecommendationSubjectIdDeriver>();
builder.Services.AddScoped<
    IRecommendationSubjectBackfillStore,
    SqlServerRecommendationSubjectBackfillStore>();
builder.Services.AddScoped<
    IRecommendationSubjectBackfillService,
    RecommendationSubjectBackfillService>();
builder.Services.AddScoped<
    IPopularityRecommendationStore,
    SqlServerPopularityRecommendationStore>();
builder.Services.AddScoped<
    IPopularityRecommendationService,
    PopularityRecommendationService>();
builder.Services.AddScoped<
    IFrequentlyBoughtTogetherRecommendationStore,
    SqlServerFrequentlyBoughtTogetherRecommendationStore>();
builder.Services.AddScoped<
    IFrequentlyBoughtTogetherRecommendationService,
    FrequentlyBoughtTogetherRecommendationService>();
builder.Services.AddScoped<
    ICartCompletionRecommendationStore,
    SqlServerCartCompletionRecommendationStore>();
builder.Services.AddScoped<
    ICartCompletionRecommendationService,
    CartCompletionRecommendationService>();
builder.Services.AddScoped<
    IModelTrainingSnapshotStore,
    SqlServerModelTrainingSnapshotStore>();
builder.Services.AddScoped<
    IRecommendationModelOrchestrationService,
    RecommendationModelOrchestrationService>();
builder.Services.AddScoped<
    IModelEvaluationSnapshotStore,
    SqlServerModelEvaluationSnapshotStore>();
builder.Services.AddScoped<
    IRecommendationModelEvaluationService,
    RecommendationModelEvaluationService>();
builder.Services.AddScoped<
    ISimilarProductStore,
    SqlServerSimilarProductStore>();
builder.Services.AddScoped<
    ISimilarRecommendationService,
    SimilarRecommendationService>();
builder.Services.AddScoped<
    IPersonalizedRecommendationStore,
    SqlServerPersonalizedRecommendationStore>();
builder.Services.AddScoped<
    IPersonalizedRecommendationService,
    PersonalizedRecommendationService>();
builder.Services.AddSingleton<RecommendationEventValidator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RecommendationModelCircuitBreaker>();
builder.Services
    .AddOptions<RecommendationSubjectOptions>()
    .Bind(builder.Configuration.GetSection(
        RecommendationSubjectOptions.SectionName))
    .Validate(
        options => options.IsValid(),
        $"Configuration section '{RecommendationSubjectOptions.SectionName}' is invalid. " +
        $"Provide a key of at least {RecommendationSubjectOptions.MinimumKeySizeBytes} UTF-8 bytes and a version such as 'v1'.")
    .ValidateOnStart();
builder.Services
    .AddOptions<PopularityRecommendationOptions>()
    .Bind(builder.Configuration.GetSection(
        PopularityRecommendationOptions.SectionName))
    .Validate(
        options => options.IsValid(),
        $"Configuration section '{PopularityRecommendationOptions.SectionName}' is invalid.")
    .ValidateOnStart();
builder.Services
    .AddOptions<SimilarRecommendationOptions>()
    .Bind(builder.Configuration.GetSection(
        SimilarRecommendationOptions.SectionName))
    .Validate(
        options => options.IsValid(),
        $"Configuration section '{SimilarRecommendationOptions.SectionName}' is invalid.")
    .ValidateOnStart();
builder.Services
    .AddOptions<PersonalizedRecommendationOptions>()
    .Bind(builder.Configuration.GetSection(
        PersonalizedRecommendationOptions.SectionName))
    .Validate(
        options => options.IsValid(),
        $"Configuration section '{PersonalizedRecommendationOptions.SectionName}' is invalid.")
    .ValidateOnStart();
builder.Services
    .AddOptions<RecommendationModelServiceOptions>()
    .Bind(builder.Configuration.GetSection(
        RecommendationModelServiceOptions.SectionName))
    .Validate(
        options => options.IsValid(),
        $"Configuration section '{RecommendationModelServiceOptions.SectionName}' is invalid.")
    .ValidateOnStart();
builder.Services.AddHttpClient<
    IRecommendationModelClient,
    RecommendationModelClient>((services, client) =>
    {
        var options = services
            .GetRequiredService<
                Microsoft.Extensions.Options.IOptions<
                    RecommendationModelServiceOptions>>()
            .Value;
        client.BaseAddress = new Uri(options.BaseAddress, UriKind.Absolute);
        client.Timeout = Timeout.InfiniteTimeSpan;
    });
builder.Services
    .AddOptions<CartCompletionRecommendationOptions>()
    .Bind(builder.Configuration.GetSection(
        CartCompletionRecommendationOptions.SectionName))
    .Validate(
        options => options.IsValid(),
        $"Configuration section '{CartCompletionRecommendationOptions.SectionName}' is invalid.")
    .ValidateOnStart();
builder.Services
    .AddOptions<FrequentlyBoughtTogetherOptions>()
    .Bind(builder.Configuration.GetSection(
        FrequentlyBoughtTogetherOptions.SectionName))
    .Validate(
        options => options.IsValid(),
        $"Configuration section '{FrequentlyBoughtTogetherOptions.SectionName}' is invalid.")
    .ValidateOnStart();
builder.Services
    .AddAuthentication(ApiKeyDefaults.Scheme)
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyDefaults.Scheme,
        options => options.ApiKey =
            builder.Configuration["Security:ApiKey"] ?? string.Empty);
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder(
            ApiKeyDefaults.Scheme)
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddExceptionHandler<GlobalApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.UnmappedMemberHandling =
            JsonUnmappedMemberHandling.Disallow;
    });
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(item => item.Value?.Errors.Count > 0)
            .ToDictionary(
                item => item.Key,
                item => item.Value!.Errors
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "The JSON request is invalid."
                        : error.ErrorMessage)
                    .ToArray(),
                StringComparer.Ordinal);
        return new BadRequestObjectResult(
            new ApiValidationErrorResponse(
                "Validation.Failed",
                "Request validation failed.",
                false,
                errors));
    };
});
const string ApiKeySecuritySchemeId = "ApiKey";

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "Online Market Recommendation API";
        document.Info.Description =
            "Ingests product and order events from the OnlineMarket.Web outbox "
            + "and serves popularity, frequently-bought-together, cart-completion, "
            + "similar-product and personalized recommendations, plus model "
            + "training/evaluation and recommendation-subject operations.";

        var components = document.Components ??= new OpenApiComponents();
        components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        components.SecuritySchemes[ApiKeySecuritySchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = ApiKeyDefaults.HeaderName,
            In = ParameterLocation.Header,
            Description =
                "API key required in the X-Api-Key header for protected endpoints."
        };

        return Task.CompletedTask;
    });

    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        var endpointMetadata =
            context.Description.ActionDescriptor.EndpointMetadata;
        var requiresApiKey = endpointMetadata.OfType<IAuthorizeData>().Any();
        var allowsAnonymous = endpointMetadata.OfType<IAllowAnonymous>().Any();

        if (requiresApiKey && !allowsAnonymous)
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(
                    ApiKeySecuritySchemeId,
                    context.Document)] = []
            });
        }

        return Task.CompletedTask;
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options =>
        options.WithTitle("Online Market Recommendation API"))
        .AllowAnonymous();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
