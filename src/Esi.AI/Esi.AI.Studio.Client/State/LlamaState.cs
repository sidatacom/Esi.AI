using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

public sealed class LlamaState
{
    public LlamaAdvancedState Advanced { get; } = new();
    public string ModelPath { get; set; } = string.Empty;
    public string? MmprojPath { get; set; }
    public string Backend { get; set; } = "Vulkan";
    public int GpuLayerCount { get; set; } = -1;
    public uint ContextSize { get; set; } = 131072;
    public Dictionary<string, BackendDeviceSetting> BackendDevices { get; } = new(StringComparer.OrdinalIgnoreCase);
}