using Esi.AI.LiteLlm.Components;
using Esi.AI.LiteLlm.Server;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddSingleton<Esi.AI.LiteLlm.Server.IChatCompletionProvider, Esi.AI.LiteLlm.Server.EchoChatCompletionProvider>();
builder.Services.AddSingleton<Esi.AI.LiteLlm.IChatCompletionService, ChatCompletionService>();
builder.Services.AddSingleton<ILazyConnectionMultiplexer>(sp => new Lazy<ConnectionMultiplexer>(() =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!)));
builder.Services.AddSingleton<IRedisCacheService>(sp => sp.GetRequiredService<ILazyConnectionMultiplexer>().Value.GetDatabase());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapPost("/api/chat", async ([FromBody] Esi.AI.LiteLlm.Client.Contracts.ChatCompletionRequest request,
                                Esi.AI.LiteLlm.IChatCompletionService service,
                                CancellationToken ct) =>
{
    if (request?.Messages?.Any() != true)
        return Results.BadRequest("Messages must contain at least one entry.");
    if (string.IsNullOrWhiteSpace(request.Model))
        return Results.BadRequest("Model must be specified.");

    var result = await service.CompleteAsync(request, ct);

    if (result.Error != null)
        return Results.Problem(result.Error.Reason,
            detail: result.Error.Code,
            statusCode: result.Error.StatusCode);

    return Results.Ok(result);
});

app.MapPost("/api/chat/stream", async (HttpContext httpContext,
                                       [FromBody] Esi.AI.LiteLlm.Client.Contracts.ChatCompletionRequest request,
                                       Esi.AI.LiteLlm.IChatCompletionService service,
                                       CancellationToken ct) =>
{
    if (request?.Messages?.Any() != true)
        return Results.BadRequest("Messages must contain at least one entry.");
    if (string.IsNullOrWhiteSpace(request.Model))
        return Results.BadRequest("Model must be specified.");

    var response = httpContext.Response;
    var writer = response.Writer;
    var chunkId = Guid.NewGuid().ToString("N");

    response.StatusCode = 200;
    response.ContentType = "text/event-stream";
    response.Headers.Append("Cache-Control", "no-cache");
    response.Headers.Append("Connection", "keep-alive");
    response.Headers.Append("X-Accel-Buffering", "no");

    try
    {
        await service.CompleteStreamingAsync(request, (token, _meta) =>
        {
            if (ct.IsCancellationRequested) return;
            var contentDelta = token ?? string.Empty;
            var chunk = new ChatCompletionChunk(chunkId, contentDelta, request.Model);
            var json = System.Text.Json.JsonSerializer.Serialize(chunk);
            writer.Write($"data: {json}\n");
            writer.Flush();
        }, ct);

        if (!ct.IsCancellationRequested)
        {
            await writer.WriteLineAsync("data: [DONE]\n", ct);
            await writer.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
    }
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Esi.AI.LiteLlm.Client._Imports).Assembly);

app.Run();

sealed class ChatCompletionChunk(string id, string contentDelta, string model)
{
    public string Id { get; init; } = id;

    [System.Text.Json.Serialization.JsonPropertyName("object")]
    public string Object { get; init; } = "chat.completion.chunk";

    [System.Text.Json.Serialization.JsonPropertyName("model")]
    public string Model { get; init; } = model;

    [System.Text.Json.Serialization.JsonPropertyName("choices")]
    public ChatCompletionChunk.Choice[] Choices { get; init; } =
        [new ChatCompletionChunk.Choice { Index = 0, Delta = new ChatCompletionChunk.Delta { Role = "assistant", Content = contentDelta } }];

    public sealed class Choice
    {
        [System.Text.Json.Serialization.JsonPropertyName("index")] public int Index { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("delta")] public Delta Delta { get; init; } = default!;
    }

    public sealed class Delta
    {
        [System.Text.Json.Serialization.JsonPropertyName("role")] public string Role { get; init; } = default!;
        [System.Text.Json.Serialization.JsonPropertyName("content")] public string? Content { get; init; }
    }
}
