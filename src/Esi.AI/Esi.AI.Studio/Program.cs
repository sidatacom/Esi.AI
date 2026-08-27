using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
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
using Esi.AI.Core.ModelLoading;
using Esi.AI.Core.Chat;
using Esi.AI.Models;

var openVinoNativeDirectory = Path.Combine(AppContext.BaseDirectory, "runtimes", "linux-x64", "native");
if (OperatingSystem.IsLinux() && Directory.Exists(openVinoNativeDirectory))
{
    var currentLibraryPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
    var libraryPath = string.IsNullOrWhiteSpace(currentLibraryPath)
        ? openVinoNativeDirectory
        : $"{openVinoNativeDirectory}{Path.PathSeparator}{currentLibraryPath}";
    Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", libraryPath);
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();
builder.Services.AddControllers();
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
builder.Services.AddSingleton<OpenVinoDiagnosticsService>();
builder.Services.AddSingleton<OpenVinoDriverInstaller>();
builder.Services.AddSingleton<OpenVinoModelLoader>();
builder.Services.AddScoped<IModelDownloadEvents, ServerModelDownloadEvents>();
builder.Services.AddHttpClient("HuggingFace", client =>
{
    client.BaseAddress = new Uri("https://huggingface.co/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Esi.AI.Studio/1.0");
});
builder.Services.Configure<ModelLibraryOptions>(builder.Configuration.GetSection("ModelLibrary"));
builder.Services.AddSingleton<ModelLibraryService>(services =>
    new ModelLibraryService(
        services.GetRequiredService<IHttpClientFactory>().CreateClient("HuggingFace"),
        services.GetRequiredService<IHubContext<DataHub>>(),
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

