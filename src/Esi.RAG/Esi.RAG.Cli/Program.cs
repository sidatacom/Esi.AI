using System.CommandLine;
using System.Text.Json;
using Esi.RAG.Application;
using Esi.RAG.Domain;
using Esi.RAG.Infrastructure;
using Esi.RAG.Ingestion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((_, config) =>
    {
        config.SetBasePath(AppContext.BaseDirectory);
        config.AddJsonFile("appsettings.json", optional: true);
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        services.AddRagInfrastructure(context.Configuration);
        services.AddTenantRagInfrastructure(context.Configuration);
        services.AddRagApplication();
        services.AddRagIngestion();
        services.AddTenantRagIngestion();
        services.AddTenantRagSearch();
    })
    .Build();

var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

var repositoryPathArgument = new Argument<string?>("repositoryPath") { Arity = ArgumentArity.ZeroOrOne };
var queryArgument = new Argument<string>("query");
var limitOption = new Option<int>("--limit", () => 5, "Maximum number of citations to return");
var languagesOption = new Option<string[]>("--language", () => [], "Restrict results to one or more languages") { AllowMultipleArgumentsPerToken = true };
var pathPrefixOption = new Option<string[]>("--path-prefix", () => [], "Restrict results to one or more relative path prefixes") { AllowMultipleArgumentsPerToken = true };
var maxRoundsOption = new Option<int>("--max-rounds", () => 3, "Maximum bounded investigation rounds");
var maxCitationsOption = new Option<int>("--max-citations", () => 12, "Maximum total citations across all rounds");

var healthCommand = new Command("health", "Report overall and per-dependency health");
healthCommand.SetHandler(async () =>
{
    using var scope = host.Services.CreateScope();
    var healthService = scope.ServiceProvider.GetRequiredService<IHealthService>();
    var overall = await healthService.CheckAsync(CancellationToken.None);
    var qdrant = await healthService.CheckQdrantAsync(CancellationToken.None);
    var lmStudio = await healthService.CheckLmStudioAsync(CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(new { overall, qdrant, lmStudio }, jsonOptions));
});

var indexCommand = new Command("index", "Discover, extract, embed, and index a repository") { repositoryPathArgument };
indexCommand.SetHandler(async (string? repositoryPath) =>
{
    using var scope = host.Services.CreateScope();
    var ingestionService = scope.ServiceProvider.GetRequiredService<IIngestionService>();
    var progress = new Progress<IngestionProgress>(value =>
        Console.Error.WriteLine($"Indexing {value.FilesProcessed}/{value.TotalFiles} files; skipped {value.FilesSkipped}; failed {value.FilesFailed}; embedded {value.ChunksEmbedded}; uploaded {value.ChunksUploaded}; current: {value.CurrentFile ?? "done"}"));
    var result = await ingestionService.IngestAsync(new IngestionRequest(repositoryPath ?? string.Empty, progress), CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
}, repositoryPathArgument);

var searchCommand = new Command("search", "Search indexed content and answer from evidence only")
{
    queryArgument, limitOption, languagesOption, pathPrefixOption,
};
searchCommand.SetHandler(async (string query, int limit, string[] languages, string[] pathPrefixes) =>
{
    using var scope = host.Services.CreateScope();
    var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();
    var filter = ToFilter(languages, pathPrefixes);
    var result = await searchService.SearchAsync(new SearchRequest(query, limit, filter), CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
}, queryArgument, limitOption, languagesOption, pathPrefixOption);

var askCommand = new Command("ask", "Run a bounded, evidence-only multi-step investigation")
{
    queryArgument, maxRoundsOption, maxCitationsOption, languagesOption, pathPrefixOption,
};
askCommand.SetHandler(async (string query, int maxRounds, int maxCitations, string[] languages, string[] pathPrefixes) =>
{
    using var scope = host.Services.CreateScope();
    var askService = scope.ServiceProvider.GetRequiredService<IAskService>();
    var filter = ToFilter(languages, pathPrefixes);
    var result = await askService.AskAsync(new AskRequest(query, maxRounds, maxCitations, filter), CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
}, queryArgument, maxRoundsOption, maxCitationsOption, languagesOption, pathPrefixOption);

var rootCommand = new RootCommand("Esi.RAG CLI - local, evidence-only retrieval over a source repository")
{
    healthCommand, indexCommand, searchCommand, askCommand,
};

return await rootCommand.InvokeAsync(args);

static SearchFilter? ToFilter(string[] languages, string[] pathPrefixes) =>
    (languages.Length > 0 || pathPrefixes.Length > 0) ? new SearchFilter(languages, pathPrefixes) : null;

