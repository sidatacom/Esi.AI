using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Esi.AI.Studio.Client.Pages;
using Esi.AI.Studio.Components;
using Esi.AI.Studio.Components.Account;
using Esi.AI.Studio.Data;
using Esi.AI.Studio.Hubs;
using Esi.AI.Studio.Services;
using Esi.AI.Studio.Client.Services;
using Esi.AI.Llm.ModelLoading;
using Esi.AI.Llm.Chat;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<HttpClient>(serviceProvider =>
{
    var request = serviceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext?.Request
        ?? throw new InvalidOperationException("No active HTTP request is available.");
    return new HttpClient
    {
        BaseAddress = new Uri($"{request.Scheme}://{request.Host}/")
    };
});

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString), ServiceLifetime.Scoped);
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
builder.Services.AddSingleton<LlamaModelLoader>();
builder.Services.AddSingleton<ILlamaControlService, ServerLlamaControlService>();
builder.Services.AddHttpClient("HuggingFace", client =>
{
    client.BaseAddress = new Uri("https://huggingface.co/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Esi.AI.Studio/1.0");
});
builder.Services.Configure<ModelLibraryOptions>(builder.Configuration.GetSection("ModelLibrary"));
builder.Services.AddSingleton<ModelLibraryService>(services =>
    new ModelLibraryService(
        services.GetRequiredService<IHttpClientFactory>().CreateClient("HuggingFace"),
        services.GetRequiredService<IOptions<ModelLibraryOptions>>()));
builder.Services.AddScoped<DataService>();
builder.Services.AddScoped<IDataService>(services => services.GetRequiredService<DataService>());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();

    var library = scope.ServiceProvider.GetRequiredService<ModelLibraryService>();
    var dataService = scope.ServiceProvider.GetRequiredService<DataService>();
    var models = await library.ScanLocalModelsAsync();
    await dataService.SyncLlamaModelsAsync(models.Select(model => new LlamaModel(
        Guid.Empty, model.Name, model.Path, model.SizeInBytes, model.LastWriteTimeUtc)).ToArray());
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapOpenApi();
app.UseSwagger();
app.UseSwaggerUI();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Esi.AI.Studio.Client._Imports).Assembly);
app.MapHub<DataHub>("/hubs/data");

app.MapPost("/api/llama/load", async (LoadModelRequest request, LlamaModelLoader modelLoader) =>
{
    try
    {
        var advanced = request.Advanced;
        await modelLoader.LoadAsync(request.ModelPath, request.Backend, request.GpuLayerCount, request.ContextSize, request.VulkanDeviceWeights,
            new LlamaLoadOptions(advanced.MainGpu, advanced.SeqMax, advanced.RecurrentRollbackSnapshots, advanced.UseMemorymap,
                advanced.UseDirectIO, advanced.UseMemoryLock, advanced.Threads, advanced.BatchThreads, advanced.BatchSize,
                advanced.UBatchSize, advanced.Embeddings, advanced.NoKqvOffload, advanced.FlashAttention, advanced.VocabOnly,
                advanced.OpOffload, advanced.SwaFull, advanced.KVUnified, advanced.RopeFrequencyBase, advanced.RopeFrequencyScale,
                advanced.YarnExtrapolationFactor, advanced.YarnAttentionFactor, advanced.YarnBetaFast, advanced.YarnBetaSlow,
                advanced.YarnOriginalContext));
        return Results.Ok(modelLoader.Status);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(exception.Message);
    }
    catch (FileNotFoundException exception)
    {
        return Results.NotFound(exception.Message);
    }
    catch (LLama.Exceptions.LoadWeightsFailedException exception)
    {
        return Results.Json(new
        {
            error = exception.Message,
            nativeLog = modelLoader.GetStatus().LoadLog
        }, statusCode: StatusCodes.Status422UnprocessableEntity);
    }
});

app.MapGet("/api/llama/status", (LlamaModelLoader modelLoader) =>
    Results.Ok(modelLoader.GetStatus()));

app.MapPost("/api/llama/unload", async (LlamaModelLoader modelLoader) =>
{
    await modelLoader.StopAsync();
    return Results.Ok(modelLoader.GetStatus());
});

app.MapGet("/api/models/local", async (ModelLibraryService library, DataService dataService, CancellationToken cancellationToken) =>
{
    var models = await library.ScanLocalModelsAsync(cancellationToken);
    await dataService.SyncLlamaModelsAsync(models.Select(model => new LlamaModel(
        Guid.Empty, model.Name, model.Path, model.SizeInBytes, model.LastWriteTimeUtc)).ToArray(), cancellationToken);
    return Results.Ok(models);
});

app.MapGet("/api/models/directories", (ModelLibraryService library) =>
    Results.Ok(library.GetModelDirectories()));

app.MapGet("/api/models/search", async (string? query, ModelLibraryService library, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await library.SearchHuggingFaceAsync(query ?? string.Empty, cancellationToken));
    }
    catch (HttpRequestException exception)
    {
        return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapPost("/api/models/download", async (ModelDownloadRequest request, ModelLibraryService library, CancellationToken cancellationToken) =>
{
    try
    {
        var id = await library.StartDownloadAsync(request.ModelId, request.FileName, cancellationToken);
        return Results.Accepted($"/api/models/downloads/{id}", new { id });
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(exception.Message);
    }
    catch (InvalidOperationException exception)
    {
        return Results.UnprocessableEntity(exception.Message);
    }
});

app.MapGet("/api/models/downloads/{id:guid}", (Guid id, ModelLibraryService library) =>
{
    var status = library.GetDownload(id);
    return status is null ? Results.NotFound() : Results.Ok(status);
});

app.MapPost("/api/models/select", async (SelectModelRequest request, IDataService dataService, CancellationToken cancellationToken) =>
{
    if (!Path.IsPathFullyQualified(request.Path) || !request.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("A fully qualified GGUF path is required.");

    var settings = await dataService.GetLlamaSettingsAsync(cancellationToken) ?? new LlamaSettings(
        request.Path, "Vulkan", 0, (uint)LlamaContextSize.Context128K,
        new Dictionary<string, VulkanDeviceSetting>(StringComparer.OrdinalIgnoreCase));
    await dataService.SaveLlamaSettingsAsync(settings with { ModelPath = request.Path }, cancellationToken);
    return Results.Ok(new { path = request.Path });
});

app.MapGet("/api/chats", async (DataService dataService, CancellationToken cancellationToken) =>
    Results.Ok(await dataService.GetChatSummariesAsync(cancellationToken)));

app.MapPost("/api/chats", async (CreateChatRequest request, DataService dataService, CancellationToken cancellationToken) =>
    Results.Ok(await dataService.CreateChatAsync(request.Title, cancellationToken)));

app.MapGet("/api/chats/{id:guid}", async (Guid id, DataService dataService, CancellationToken cancellationToken) =>
{
    var chat = await dataService.GetChatAsync(id, cancellationToken);
    return chat is null ? Results.NotFound() : Results.Ok(chat);
});

app.MapPost("/api/chats/{id:guid}/messages", async (Guid id, ChatExchangeRequest request, DataService dataService, LlamaModelLoader modelLoader, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Content))
        return Results.BadRequest("A chat message is required.");

    if (string.IsNullOrWhiteSpace(request.ModelPath))
        return Results.BadRequest("A loaded model must be selected.");

    var chat = await dataService.GetChatAsync(id, cancellationToken);
    if (chat is null)
        return Results.NotFound();

    try
    {
        using var session = modelLoader.CreateChatSession("You are a helpful assistant.", request.ModelPath);
        var messages = chat.Messages.Select(message => new LlamaChatMessage(message.Role, message.Content))
            .Append(new LlamaChatMessage("user", request.Content.Trim())).ToArray();
        var generation = await session.GenerateWithStatsAsync(messages, cancellationToken);
        return Results.Ok(await dataService.AddChatExchangeAsync(id, request.Content.Trim(), generation, request.ModelPath, cancellationToken));
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(exception.Message);
    }
});

app.MapPost("/api/chat", async (ChatRequest request, LlamaModelLoader modelLoader, CancellationToken cancellationToken) =>
{
    if (request.Messages is null || request.Messages.Count == 0)
        return Results.BadRequest("At least one chat message is required.");

    try
    {
        using var session = modelLoader.CreateChatSession(request.SystemPrompt ?? "You are a helpful assistant.");
        var response = await session.GenerateAsync(
            request.Messages.Select(message => new LlamaChatMessage(message.Role, message.Content)).ToArray(),
            cancellationToken);
        return Results.Ok(new ChatResponse(response));
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(exception.Message);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(exception.Message);
    }
});

app.MapGet("/v1/models", (LlamaModelLoader modelLoader) =>
{
    var status = modelLoader.GetStatus();
    var modelId = status.ModelPath is null ? "local-model" : Path.GetFileNameWithoutExtension(status.ModelPath);
    return Results.Ok(new
    {
        @object = "list",
        data = new[] { new { id = modelId, @object = "model", created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), owned_by = "esi-ai" } }
    });
})
    .WithTags("OpenAI Compatible API")
    .WithSummary("List available models")
    .WithDescription("Returns the currently configured local model in OpenAI model-list format.");

app.MapPost("/v1/chat/completions", async (OpenAiChatRequest request, HttpResponse response, LlamaModelLoader modelLoader, CancellationToken cancellationToken) =>
{
    if (request.Messages is null || request.Messages.Count == 0)
        return Results.BadRequest(new { error = new { message = "At least one chat message is required.", type = "invalid_request_error" } });

    try
    {
        using var session = modelLoader.CreateChatSession("You are a helpful assistant.");
        var messages = request.Messages.Select(message => new LlamaChatMessage(message.Role, message.Content)).ToArray();
        var model = request.Model ?? "local-model";

        if (!request.Stream)
        {
            var content = await session.GenerateAsync(messages, cancellationToken);
            return Results.Ok(new
            {
                id = $"chatcmpl-{Guid.NewGuid():N}",
                @object = "chat.completion",
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                model,
                choices = new[] { new { index = 0, message = new { role = "assistant", content }, finish_reason = "stop" } }
            });
        }

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        var completionId = $"chatcmpl-{Guid.NewGuid():N}";
        await foreach (var token in session.GenerateStreamingAsync(messages, cancellationToken))
        {
            var chunk = new
            {
                id = completionId,
                @object = "chat.completion.chunk",
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                model,
                choices = new[] { new { index = 0, delta = new { content = token }, finish_reason = (string?)null } }
            };
            await response.WriteAsync($"data: {JsonSerializer.Serialize(chunk)}\n\n", cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }

        await response.WriteAsync("data: [DONE]\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
        return Results.Empty;
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = new { message = exception.Message, type = "server_error" } });
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = new { message = exception.Message, type = "invalid_request_error" } });
    }
})
    .WithTags("OpenAI Compatible API")
    .WithSummary("Create a chat completion")
    .WithDescription("Generates a completion from the active local LLamaSharp model. Set stream to true for Server-Sent Events.");

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.Run();

internal sealed record LoadModelRequest(string ModelPath, string Backend, int GpuLayerCount, uint ContextSize, IReadOnlyDictionary<string, float> VulkanDeviceWeights, LlamaAdvancedSettings? AdvancedSettings)
{
    public LlamaAdvancedSettings Advanced { get; } = AdvancedSettings ?? new();
}

internal sealed record ChatRequest(IReadOnlyList<ChatMessageRequest> Messages, string? SystemPrompt = null);

internal sealed record ChatMessageRequest(string Role, string Content);

internal sealed record ChatResponse(string Content);

internal sealed record CreateChatRequest(string? Title = null);

internal sealed record ChatExchangeRequest(string Content, string? ModelPath = null);

internal sealed record OpenAiChatRequest(string? Model, IReadOnlyList<OpenAiChatMessage> Messages, bool Stream = false);

internal sealed record OpenAiChatMessage(string Role, string Content);

internal sealed record ModelDownloadRequest(string ModelId, string? FileName = null);

internal sealed record SelectModelRequest(string Path);
