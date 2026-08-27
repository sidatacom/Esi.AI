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
builder.Services.AddScoped<SignalRDataService>();
builder.Services.AddScoped<IDataService>(services => services.GetRequiredService<SignalRDataService>());
builder.Services.AddScoped<IModelDownloadEvents>(services => services.GetRequiredService<SignalRDataService>());

await builder.Build().RunAsync();
