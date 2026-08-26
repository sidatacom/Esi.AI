namespace Esi.AI.Studio.Data;

public sealed class LlamaSettingsEntity
{
    public int Id { get; set; }

    public string ModelPath { get; set; } = string.Empty;

    public string Backend { get; set; } = "Vulkan";

    public int GpuLayerCount { get; set; }

    public uint ContextSize { get; set; }

    public string VulkanDeviceWeightsJson { get; set; } = "{}";

    public string AdvancedSettingsJson { get; set; } = "{}";

    public Guid? ConfigurationProfileId { get; set; }
}