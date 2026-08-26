using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Components;

namespace Esi.AI.Studio.Client.Services;

public sealed class SignalRDataService : IDataService, ILlamaControlService, IAsyncDisposable
{
    private readonly HubConnection connection;

    public SignalRDataService(NavigationManager navigationManager)
    {
        connection = new HubConnectionBuilder()
            .WithUrl(navigationManager.ToAbsoluteUri("/hubs/data"))
            .WithAutomaticReconnect()
            .Build();
    }

    public async Task<LlamaSettings?> GetLlamaSettingsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<LlamaSettings?>("GetLlamaSettings", cancellationToken);
    }

    public async Task SaveLlamaSettingsAsync(LlamaSettings settings, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("SaveLlamaSettings", settings, cancellationToken);
    }

    public async Task<IReadOnlyList<LlamaModel>> GetLlamaModelsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<LlamaModel>>("GetLlamaModels", cancellationToken);
    }

    public async Task<IReadOnlyList<LlamaModel>> ScanLlamaModelsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<LlamaModel>>("ScanLlamaModels", cancellationToken);
    }

    public async Task SyncLlamaModelsAsync(IReadOnlyList<LlamaModel> models, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("SyncLlamaModels", models, cancellationToken);
    }

    public async Task<IReadOnlyList<LlamaConfigurationProfile>> GetLlamaConfigurationProfilesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<LlamaConfigurationProfile>>("GetLlamaConfigurationProfiles", cancellationToken);
    }

    public async Task<LlamaConfigurationProfile?> GetLlamaConfigurationProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<LlamaConfigurationProfile?>("GetLlamaConfigurationProfile", id, cancellationToken);
    }

    public async Task<LlamaConfigurationProfile> SaveLlamaConfigurationProfileAsync(LlamaConfigurationProfile profile, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<LlamaConfigurationProfile>("SaveLlamaConfigurationProfile", profile, cancellationToken);
    }

    public async Task DeleteLlamaConfigurationProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("DeleteLlamaConfigurationProfile", id, cancellationToken);
    }

    public async Task SetDefaultLlamaConfigurationProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("SetDefaultLlamaConfigurationProfile", id, cancellationToken);
    }

    public async Task<ModelLoadStatus> GetModelStatusAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<ModelLoadStatus>("GetModelStatus", cancellationToken);
    }

    public async Task<ModelLoadStatus> LoadModelAsync(LoadModelRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<ModelLoadStatus>("LoadModel", request, cancellationToken);
    }

    public async Task<ModelLoadStatus> UnloadModelAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<ModelLoadStatus>("UnloadModel", cancellationToken);
    }

    public async Task<ModelLoadStatus> UnloadModelAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<ModelLoadStatus>("UnloadModelByPath", modelPath, cancellationToken);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (connection.State == HubConnectionState.Disconnected)
            await connection.StartAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await connection.DisposeAsync();
    }
}