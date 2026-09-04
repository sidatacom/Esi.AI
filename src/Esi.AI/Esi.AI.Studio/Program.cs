using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;
using Esi.AI.Studio.Client.Pages;
using Esi.AI.Studio.Components;
using Esi.AI.Studio.Components.Account;
using Esi.AI.Studio.Data;
using Esi.AI.Studio.Hubs;
using Esi.AI.Studio.Services;
using Esi.AI.Studio.Contracts;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Core.Chat;
using Esi.AI.Models;
using Esi.AI.Studio;

EnsureStudioWatchdogIsRunning();
StudioProcessIsolation.Configure();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();
builder.Services.AddControllers();
builder.Services.AddSignalR(options =>
{
    options.MaximumParallelInvocationsPerClient = 8;
    options.MaximumReceiveMessageSize = 8 * 1024 * 1024;
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});
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
    .AddIdentityCookies(options =>
    {
        options.ApplicationCookie?.Configure(cookieOptions =>
        {
            cookieOptions.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
            cookieOptions.Events.OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
        });
    });

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
builder.Services.AddSingleton<OpenVinoDiagnosticsService>();
builder.Services.AddSingleton<OpenVinoDriverInstaller>();
builder.Services.AddSingleton<IBackendDiagnosticsService, BackendDiagnosticsService>();
builder.Services.Configure<BackendRuntimeOptions>(builder.Configuration.GetSection("BackendRuntime"));
builder.Services.AddHttpClient("BackendRuntime", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Esi.AI.Studio/1.0");
    client.Timeout = TimeSpan.FromMinutes(30);
});
builder.Services.AddSingleton<BackendRuntimeInstaller>(services =>
    new BackendRuntimeInstaller(
        services.GetRequiredService<IHttpClientFactory>().CreateClient("BackendRuntime"),
        services.GetRequiredService<IOptions<BackendRuntimeOptions>>()));
builder.Services.AddSingleton<BackendPrerequisiteProvisioner>();
builder.Services.AddSingleton<BackendRequirementMonitor>();
builder.Services.AddHostedService(services => services.GetRequiredService<BackendRequirementMonitor>());
builder.Services.AddSingleton<IBackendRequirementState>(services => services.GetRequiredService<BackendRequirementMonitor>());
builder.Services.AddHostedService<StudioWatchdogLease>();
builder.Services.AddSingleton<IModelRuntimeStatusPublisher, SignalRModelRuntimeStatusPublisher>();
builder.Services.AddSingleton<IBackendRuntimeStatusPublisher, SignalRBackendRuntimeStatusPublisher>();
builder.Services.AddSingleton<ModelRuntime>();
builder.Services.AddHostedService(services => services.GetRequiredService<ModelRuntime>());
builder.Services.AddScoped<IModelDownloadEvents, ServerModelDownloadEvents>();
builder.Services.AddScoped<IModelRuntimeEvents, ServerModelDownloadEvents>();
builder.Services.AddScoped<IBackendRequirementEvents, ServerModelDownloadEvents>();
builder.Services.AddHttpClient("HuggingFace", client =>
{
    client.BaseAddress = new Uri("https://huggingface.co/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Esi.AI.Studio/1.0");
    var token = builder.Configuration["ModelLibrary:HuggingFaceToken"] ?? Environment.GetEnvironmentVariable("HF_TOKEN");
    if (!string.IsNullOrWhiteSpace(token))
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
});
builder.Services.Configure<OmniRouteOptions>(builder.Configuration.GetSection("OmniRoute"));
builder.Services.AddHttpClient<IOmniRouteClient, OmniRouteClient>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<OmniRouteOptions>>().Value;
    if (!Uri.TryCreate(options.BaseUrl.TrimEnd('/') + "/v1/", UriKind.Absolute, out var baseUri))
        throw new InvalidOperationException("OmniRoute:BaseUrl must be an absolute URI.");

    client.BaseAddress = baseUri;
    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
});
builder.Services.Configure<ModelLibraryOptions>(builder.Configuration.GetSection("ModelLibrary"));
builder.Services.AddSingleton<ILocalModelScanner, LocalModelScanner>();
builder.Services.AddSingleton<ModelLibraryService>(services =>
    new ModelLibraryService(
        services.GetRequiredService<IHttpClientFactory>().CreateClient("HuggingFace"),
        services.GetRequiredService<IHubContext<DataHub>>(),
        services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(),
        services.GetRequiredService<IOptions<ModelLibraryOptions>>(),
        services.GetRequiredService<ILocalModelScanner>()));
builder.Services.AddSingleton<ILocalModelCatalog>(services => services.GetRequiredService<ModelLibraryService>());
builder.Services.AddSingleton<IModelDirectoryCatalog>(services => services.GetRequiredService<ModelLibraryService>());
builder.Services.AddSingleton<IHuggingFaceCatalog>(services => services.GetRequiredService<ModelLibraryService>());
builder.Services.AddSingleton<IModelDownloadManager>(services => services.GetRequiredService<ModelLibraryService>());
builder.Services.AddScoped<IInferenceScheduler, InferenceScheduler>();
builder.Services.AddScoped<IInferenceService, InferenceService>();
builder.Services.AddScoped<DataService>();
    builder.Services.AddScoped<IDataService>(services => services.GetRequiredService<DataService>());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();

    var library = scope.ServiceProvider.GetRequiredService<ModelLibraryService>();
    await library.RestoreDownloadsAsync();
    var dataService = scope.ServiceProvider.GetRequiredService<DataService>();
    await dataService.Model_UpdateAsync();
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

    app.UseHttpsRedirection();    
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);   

app.UseAntiforgery();

app.MapOpenApi();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Esi.AI.Studio.Client._Imports).Assembly);
app.MapHub<DataHub>("/hubs/data");

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.Run();

static void EnsureStudioWatchdogIsRunning()
{
    var pidFile = Environment.GetEnvironmentVariable("ESI_AI_STUDIO_WATCHDOG_PID_FILE");
    if (string.IsNullOrWhiteSpace(pidFile) || !File.Exists(pidFile))
        throw new InvalidOperationException("Esi.AI Studio refuses to start without the project watchdog.");

    if (!StudioProcessIsolation.IsWatchdogAlive(pidFile))
        throw new InvalidOperationException("Esi.AI Studio refuses to start because the watchdog is not running.");
}

