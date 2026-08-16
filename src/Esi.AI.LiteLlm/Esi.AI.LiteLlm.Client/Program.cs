// Blazor WebAssembly Client entry

using Esi.AI.LiteLlm.Client;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Register App as the root Blazor component
builder.RootComponents.Add<App>("#app");

// Register the HeadOutlet component
builder.RootComponents.Add<HeadOutlet>("head::after");

// Add services required for Blazor WebAssembly
builder.Services.AddScoped<IChatCompletionProvider, Esi.AI.LiteLlm.Server.EchoChatCompletionProvider>();
builder.Services.AddScoped<ProviderRouter>();
builder.Services.AddScoped<PricingConfiguration>();

builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

await builder.Build().RunAsync();
