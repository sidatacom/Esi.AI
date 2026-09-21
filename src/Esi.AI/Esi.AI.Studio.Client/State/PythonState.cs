using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

public sealed class PythonState
{
    public string ModelPath { get; set; } = string.Empty;
    public string PythonExecutable { get; set; } = "python3";
    public string? WorkingDirectory { get; set; }
    public string Device { get; set; } = string.Empty;
    public List<string> Devices { get; } = [];
    public string AdditionalDeviceRoutes { get; set; } = string.Empty;
    public IReadOnlyList<string> SelectedDevices => GetDevices();
    public int? GpuMemoryUtilization { get; set; } = 90;
    public int MaxModelLength { get; set; } = 2048;
    public int TensorParallelSize { get; set; } = 1;
    public bool TrustRemoteCode { get; set; } = true;
    public string Quantization { get; set; } = "gptq";
    public string DType { get; set; } = "float16";
    public string KvCacheDType { get; set; } = "fp8";
    public int MtpTokens { get; set; } = 4;
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
        EnablePrefixCaching = request.EnablePrefixCaching; EnableXpuGraph = request.EnableXpuGraph; EnableBf16MtpDraft = request.EnableBf16MtpDraft;
    }

    public PythonInferenceLoadRequest ToRequest(ConfigurationBackend backend) => new(ModelPath, backend, PythonExecutable, WorkingDirectory, 8000, GpuMemoryUtilization, (uint)Math.Max(1, MaxModelLength), Math.Max(1, TensorParallelSize), TrustRemoteCode, null, (uint)Math.Max(0, MtpTokens), .7f, .9f, !EnableXpuGraph, GetDevices().FirstOrDefault() ?? Device, GetDevices(), Quantization, DType, KvCacheDType, MtpTokens > 0 ? $"{{\"method\":\"qwen3_5_mtp\",\"num_speculative_tokens\":{MtpTokens}}}" : string.Empty, (uint)Math.Max(0, MaxNumSeqs), (uint)Math.Max(0, MaxNumBatchedTokens), EnablePrefixCaching, EnableXpuGraph, EnableBf16MtpDraft);

    public void Reset() { PythonExecutable = "python3"; Device = string.Empty; Devices.Clear(); AdditionalDeviceRoutes = string.Empty; GpuMemoryUtilization = 90; MaxModelLength = 2048; TensorParallelSize = 1; TrustRemoteCode = true; Quantization = string.Empty; DType = "float16"; KvCacheDType = "fp8"; MtpTokens = 4; MaxNumSeqs = 1; MaxNumBatchedTokens = 8192; EnablePrefixCaching = true; EnableXpuGraph = true; EnableBf16MtpDraft = true; }
    private IReadOnlyList<string> GetDevices() => Devices.Concat(AdditionalDeviceRoutes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Where(device => !string.IsNullOrWhiteSpace(device)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}