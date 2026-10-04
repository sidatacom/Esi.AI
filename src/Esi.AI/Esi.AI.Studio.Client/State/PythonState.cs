using System.Text.Json;
using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

public sealed class PythonState : IBackendTabConfigurationData
{
    public string ModelPath { get; set; } = string.Empty;
    public string PythonExecutable { get; set; } = "python3";
    public string? WorkingDirectory { get; set; }
    public string Device { get; set; } = string.Empty;
    public List<string> Devices { get; } = [];
    public IReadOnlyList<string> SelectedDevices => GetDevices();
    public int? GpuMemoryUtilization { get; set; } = 90;
    public int MaxModelLength { get; set; } = 2048;
    public int TensorParallelSize { get; set; } = 1;
    public bool TrustRemoteCode { get; set; } = true;
    public string Quantization { get; set; } = "gptq";
    public string DType { get; set; } = "float16";
    public string KvCacheDType { get; set; } = "fp8";
    public int MtpTokens { get; set; }
    public int MaxNumSeqs { get; set; } = 1;
    public int MaxNumBatchedTokens { get; set; } = 8192;
    public bool EnablePrefixCaching { get; set; } = true;
    public bool EnableXpuGraph { get; set; } = true;
    public bool EnableBf16MtpDraft { get; set; } = true;
    public bool IsDeviceSelected(string device) => Devices.Contains(device, StringComparer.OrdinalIgnoreCase);

    public void Load(PythonInferenceLoadRequest request)
    {
        ModelPath = request.ModelPath; PythonExecutable = request.PythonExecutable; WorkingDirectory = request.WorkingDirectory;
        Devices.Clear();
        foreach (var device in request.Devices ?? []) if (!string.IsNullOrWhiteSpace(device) && !Devices.Contains(device, StringComparer.OrdinalIgnoreCase)) Devices.Add(device.Trim());
        if (Devices.Count == 0 && !string.IsNullOrWhiteSpace(request.Device)) Devices.Add(request.Device.Trim());
        Device = Devices.FirstOrDefault() ?? string.Empty; GpuMemoryUtilization = request.GpuMemoryUtilization;
        MaxModelLength = (int)Math.Min(int.MaxValue, request.MaxModelLength); TensorParallelSize = request.TensorParallelSize; TrustRemoteCode = request.TrustRemoteCode;
        Quantization = request.Quantization; DType = request.DType; KvCacheDType = request.KvCacheDType;
        MaxNumSeqs = request.MaxNumSeqs > 0 ? (int)Math.Min(int.MaxValue, request.MaxNumSeqs) : MaxNumSeqs;
        MaxNumBatchedTokens = request.MaxNumBatchedTokens > 0 ? (int)Math.Min(int.MaxValue, request.MaxNumBatchedTokens) : MaxNumBatchedTokens;
        MtpTokens = GetMtpTokenCount(request.SpeculativeConfigJson);
        EnablePrefixCaching = request.EnablePrefixCaching; EnableXpuGraph = request.EnableXpuGraph; EnableBf16MtpDraft = request.EnableBf16MtpDraft;
    }

    public void Load(PythonInferenceLoadRequest request, string backendVariantId, string configurationJson)
    {
        Load(request);
        if (HasConfiguredVllmDeviceRoute(configurationJson))
            return;

        var defaultDevice = backendVariantId.ToLowerInvariant() switch
        {
            "vllm.cuda12" => "cuda:0",
            "vllm.xpu" => "xpu:0",
            _ => null
        };
        if (defaultDevice is null)
            return;

        Devices.Clear();
        Devices.Add(defaultDevice);
        Device = defaultDevice;
    }

    public PythonInferenceLoadRequest ToRequest(ConfigurationBackend backend) => new(ModelPath, backend, PythonExecutable, WorkingDirectory, 8000, GpuMemoryUtilization, (uint)Math.Max(1, MaxModelLength), Math.Max(1, TensorParallelSize), TrustRemoteCode, null, (uint)Math.Max(0, MtpTokens), .7f, .9f, !EnableXpuGraph, GetDevices().FirstOrDefault() ?? Device, GetDevices(), Quantization, DType, KvCacheDType, MtpTokens > 0 ? $"{{\"method\":\"qwen3_5_mtp\",\"num_speculative_tokens\":{MtpTokens}}}" : string.Empty, (uint)Math.Max(0, MaxNumSeqs), (uint)Math.Max(0, MaxNumBatchedTokens), EnablePrefixCaching, EnableXpuGraph, EnableBf16MtpDraft);

    public void Reset() { PythonExecutable = "python3"; Device = string.Empty; Devices.Clear(); GpuMemoryUtilization = 90; MaxModelLength = 2048; TensorParallelSize = 1; TrustRemoteCode = true; Quantization = string.Empty; DType = "float16"; KvCacheDType = "fp8"; MtpTokens = 0; MaxNumSeqs = 1; MaxNumBatchedTokens = 8192; EnablePrefixCaching = true; EnableXpuGraph = true; EnableBf16MtpDraft = true; }
    private IReadOnlyList<string> GetDevices() => Devices.Where(device => !string.IsNullOrWhiteSpace(device)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static int GetMtpTokenCount(string speculativeConfigJson)
    {
        if (string.IsNullOrWhiteSpace(speculativeConfigJson))
            return 0;

        using var configuration = JsonDocument.Parse(speculativeConfigJson);
        return configuration.RootElement.TryGetProperty("num_speculative_tokens", out var tokenCount) &&
            tokenCount.TryGetInt32(out var count) && count > 0
                ? count
                : 0;
    }

    private static bool HasConfiguredVllmDeviceRoute(string configurationJson)
    {
        using var configuration = JsonDocument.Parse(configurationJson);
        var root = configuration.RootElement;
        return IsVllmDeviceRoute(GetStringProperty(root, "Device")) ||
            TryGetProperty(root, "Devices", out var devices) && devices.ValueKind == JsonValueKind.Array &&
            devices.EnumerateArray().Any(device => device.ValueKind == JsonValueKind.String &&
                IsVllmDeviceRoute(device.GetString()));
    }

    private static bool IsVllmDeviceRoute(string? route) =>
        route?.StartsWith("cuda:", StringComparison.OrdinalIgnoreCase) == true ||
        route?.StartsWith("xpu:", StringComparison.OrdinalIgnoreCase) == true;

    private static string? GetStringProperty(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}