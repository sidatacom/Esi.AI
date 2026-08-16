using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Esi.AI.LiteLlm;
using Esi.AI.LiteLlm.Server;
using System.Text.Json;
using Esi.AI.LiteLlm.Client.Contracts;

var builder = WebApplication.CreateBuilder(args);

// Configure Identity
builder.Services.AddIdentityCore<IdentityUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager();

// Configure Authentication with Identity
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
    })
    .AddCookie(IdentityConstants.ApplicationScheme)
    .AddJwtBearer(options =>
    {
        // JWT Bearer token configuration
        options.Authority = builder.Configuration["Auth:Authority"];
        options.Audience = builder.Configuration["Auth:Audience"];
    });

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
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

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(BlazorApp4.Client._Imports).Assembly);

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

// OpenAI-kompatibler Chat Completion Endpoint (non-streaming)
app.MapPost("/v1/chat/completions", async (
    [FromBody] Esi.AI.LiteLlm.Client.Contracts.ChatCompletionRequest request,
    IChatCompletionService service,
    CancellationToken ct) =>
{
    if (request?.Messages?.Any() != true)
        return Results.BadRequest("Messages must contain at least one entry.");
    if (string.IsNullOrWhiteSpace(request.Model))
        return Results.BadRequest("Model must be specified.");

    var result = await service.CompleteAsync(request, ct);

    if (result.Error != null)
        return Results.Problem(result.Error.Reason,
            statusCode: result.Error.StatusCode);

    // Build OpenAI-compatible response
    var responseDict = new Dictionary<string, object>
    {
        ["id"] = Guid.NewGuid().ToString("N"),
        ["object"] = "chat.completion",
        ["model"] = request.Model,
        ["choices"] = new[]
        {
            new Dictionary<string, object>
            {
                ["index"] = 0,
                ["message"] = new Dictionary<string, object>
                {
                    ["role"] = "assistant",
                    ["content"] = result.Content!
                },
                ["finish_reason"] = result.FinishReason
            }
        }
    };

    // Add usage if available
    if (result.Usage != null)
    {
        responseDict["usage"] = new Dictionary<string, object>
        {
            ["prompt_tokens"] = result.Usage.InputTokens,
            ["completion_tokens"] = result.Usage.OutputTokens,
            ["total_tokens"] = result.Usage.TotalTokens
        };
    }

    return Results.Ok(responseDict);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
