using System.Text.Json;
using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

public sealed class OpenVinoState
{
    public string ModelPath { get; set; } = string.Empty;
    public string Device { get; set; } = "CPU";
    public Dictionary<string, OpenVinoDeviceSetting> Devices { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string CacheDirectory { get; set; } = string.Empty;
    public int MaxNewTokens { get; set; } = 128;
    public float Temperature { get; set; } = 0.7f;
    public float TopP { get; set; } = 0.9f;
    public bool DoSample { get; set; } = true;
    public int TopK { get; set; } = 50;
    public float RepetitionPenalty { get; set; } = 1.2f;
    public int MaxPromptLength { get; set; } = 1024;
    public int MinResponseLength { get; set; } = 128;
    public string PrefillHint { get; set; } = "DYNAMIC";
    public string GenerateHint { get; set; } = "FAST_COMPILE";

    public ModelSettings ToSettings(Guid? configurationId = null) => new(ModelPath, ConfigurationBackend.OpenVino, JsonSerializer.Serialize(new OpenVinoAdvancedState(Device, Devices, CacheDirectory, MaxNewTokens, Temperature, TopP, DoSample, TopK, RepetitionPenalty, new OpenVinoNpuSettings(MaxPromptLength, MinResponseLength, PrefillHint, GenerateHint))), configurationId);

    public void Load(ModelSettings settings)
    {
        var data = JsonSerializer.Deserialize<OpenVinoAdvancedState>(settings.ConfigurationJson) ?? throw new JsonException();
        ModelPath = settings.ModelPath; Device = data.Device; CacheDirectory = data.CacheDirectory; MaxNewTokens = data.MaxNewTokens;
        Temperature = data.Temperature; TopP = data.TopP; DoSample = data.DoSample; TopK = data.TopK; RepetitionPenalty = data.RepetitionPenalty;
        MaxPromptLength = data.Npu?.MaxPromptLength ?? 1024; MinResponseLength = data.Npu?.MinResponseLength ?? 128;
        PrefillHint = data.Npu?.PrefillHint ?? "DYNAMIC"; GenerateHint = data.Npu?.GenerateHint ?? "FAST_COMPILE";
        Devices.Clear();
        foreach (var device in data.Devices ?? new Dictionary<string, OpenVinoDeviceSetting>(StringComparer.OrdinalIgnoreCase)) Devices[device.Key] = device.Value;
    }
}