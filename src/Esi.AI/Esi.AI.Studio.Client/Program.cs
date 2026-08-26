using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Esi.AI.Studio.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp => new HttpClient
{
	BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();
builder.Services.AddScoped<IDataService, SignalRDataService>();
builder.Services.AddScoped<ILlamaControlService>(sp => (SignalRDataService)sp.GetRequiredService<IDataService>());

await builder.Build().RunAsync();
