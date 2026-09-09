using Esi.RAG.Api;
using Esi.RAG.Application;
using Esi.RAG.Infrastructure;
using Esi.RAG.Ingestion;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddRagInfrastructure(builder.Configuration);
builder.Services.AddTenantRagInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContextAccessor, AuthenticatedTenantContextAccessor>();
builder.Services.AddRagApplication();
builder.Services.AddRagIngestion();
builder.Services.AddTenantRagIngestion();
builder.Services.AddTenantRagSearch();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", async (IHealthService service, CancellationToken cancellationToken) => Results.Ok(await service.CheckAsync(cancellationToken)));
app.MapGet("/health/qdrant", async (IHealthService service, CancellationToken cancellationToken) =>
{
    var result = await service.CheckQdrantAsync(cancellationToken);
    return result.Healthy ? Results.Ok(result) : Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable);
});
app.MapGet("/health/lmstudio", async (IHealthService service, CancellationToken cancellationToken) =>
{
    var result = await service.CheckLmStudioAsync(cancellationToken);
    return result.Healthy ? Results.Ok(result) : Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapPost("/api/ingestion/index", async (IngestPayload payload, IIngestionService service, CancellationToken cancellationToken) =>
{
    var request = new IngestionRequest(payload.RepositoryPath ?? string.Empty);
    var result = await service.IngestAsync(request, cancellationToken);
    return Results.Ok(result);
});

app.MapPost("/api/search", async (SearchPayload payload, ISearchService service, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(payload.Query))
    {
        return Results.BadRequest("query is required");
    }

    var filter = payload.ToSearchFilter();
    var result = await service.SearchAsync(new SearchRequest(payload.Query, Math.Clamp(payload.Limit ?? 5, 1, 20), filter), cancellationToken);
    return Results.Ok(result);
});

app.MapPost("/api/tenant/sync", async (ITenantIngestionService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.SynchronizeAsync(null, cancellationToken)));

app.MapPost("/api/tenant/search", async (TenantSearchPayload payload, ITenantSearchService service, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(payload.Query))
    {
        return Results.BadRequest("query is required");
    }

    return Results.Ok(await service.SearchAsync(payload.Query, payload.Limit ?? 5, cancellationToken));
});

app.MapPost("/api/ask", async (AskPayload payload, IAskService service, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(payload.Query))
    {
        return Results.BadRequest("query is required");
    }

    var filter = payload.ToSearchFilter();
    var request = new AskRequest(payload.Query, payload.MaxRounds ?? 3, payload.MaxCitations ?? 12, filter);
    var result = await service.AskAsync(request, cancellationToken);
    return Results.Ok(result);
});

app.Run();

public sealed record IngestPayload(string? RepositoryPath);

public sealed record TenantSearchPayload(string Query, int? Limit);

public sealed record SearchPayload(string Query, int? Limit, string[]? Languages, string[]? PathPrefixes)
{
    public Esi.RAG.Domain.SearchFilter? ToSearchFilter() =>
        (Languages is { Length: > 0 } || PathPrefixes is { Length: > 0 })
            ? new Esi.RAG.Domain.SearchFilter(Languages, PathPrefixes)
            : null;
}

public sealed record AskPayload(string Query, int? MaxRounds, int? MaxCitations, string[]? Languages, string[]? PathPrefixes)
{
    public Esi.RAG.Domain.SearchFilter? ToSearchFilter() =>
        (Languages is { Length: > 0 } || PathPrefixes is { Length: > 0 })
            ? new Esi.RAG.Domain.SearchFilter(Languages, PathPrefixes)
            : null;
}

public partial class Program;

