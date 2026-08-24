using System.Diagnostics;
using System.Globalization;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using LLama;
using LLama.Common;
using LLama.Native;

namespace Esi.AI.Llm.ModelLoading;

public sealed class LlamaModelLoader : IDisposable
{
    private readonly SemaphoreSlim loadLock = new(1, 1);
    private LLamaWeights? weights;
    private bool backendConfigured;
    private readonly ConcurrentQueue<string> loadLog = new();

    public bool IsLoaded => weights is not null;

    public string? LoadedModelPath { get; private set; }

    public float Progress { get; private set; }

    public ModelLoadStatus? Status { get; private set; }

    public async Task LoadAsync(string modelPath, string backend, int gpuLayerCount, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new ArgumentException("A model path is required.", nameof(modelPath));
        }

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("The model file was not found.", modelPath);
        }

        if (!string.Equals(Path.GetExtension(modelPath), ".gguf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The model file must use the .gguf extension.", nameof(modelPath));
        }

        if (gpuLayerCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gpuLayerCount), "GPU layers cannot be negative.");
        }

        ConfigureBackend(backend);
        while (loadLog.TryDequeue(out _))
        {
        }

        await loadLock.WaitAsync(cancellationToken);

        try
        {
            Progress = 0;
            var parameters = new ModelParams(Path.GetFullPath(modelPath))
            {
                GpuLayerCount = gpuLayerCount
            };
            var loadedWeights = await LLamaWeights.LoadFromFileAsync(
                parameters,
                cancellationToken,
                new Progress<float>(value => Progress = value));

            var previousWeights = weights;
            weights = loadedWeights;
            LoadedModelPath = parameters.ModelPath;
            Status = CreateStatus(parameters.ModelPath, backend, gpuLayerCount, loadedWeights.SizeInBytes);
            previousWeights?.Dispose();
        }
        finally
        {
            loadLock.Release();
        }
    }

    private ModelLoadStatus CreateStatus(string modelPath, string backend, int gpuLayerCount, ulong modelSize)
    {
        var gpu = ReadNvidiaGpu();
        var nativeLog = string.Join(Environment.NewLine, loadLog);
        var vulkanDevices = ParseVulkanDevices(nativeLog);
        return new ModelLoadStatus(
            modelPath,
            backend.Trim().ToUpperInvariant(),
            gpuLayerCount,
            modelSize,
            gpu?.Name,
            gpu?.UsedMemoryMiB,
            gpu?.TotalMemoryMiB,
            vulkanDevices.Count,
            vulkanDevices,
            nativeLog);
    }

    private static IReadOnlyList<VulkanDeviceStatus> ParseVulkanDevices(string nativeLog)
    {
        var devices = new Dictionary<string, (int Layers, double? MemoryMiB)>();
        foreach (Match match in Regex.Matches(nativeLog, @"layer\s+\d+ assigned to device (Vulkan\d+)", RegexOptions.IgnoreCase))
        {
            var name = match.Groups[1].Value;
            devices.TryGetValue(name, out var status);
            devices[name] = (status.Layers + 1, status.MemoryMiB);
        }

        foreach (Match match in Regex.Matches(nativeLog, @"(Vulkan\d+) model buffer size\s*=\s*([0-9.]+)\s*MiB", RegexOptions.IgnoreCase))
        {
            var name = match.Groups[1].Value;
            devices.TryGetValue(name, out var status);
            devices[name] = (status.Layers, double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
        }

        return devices
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new VulkanDeviceStatus(item.Key, item.Value.Layers, item.Value.MemoryMiB))
            .ToArray();
    }

    private static NvidiaGpuStatus? ReadNvidiaGpu()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=name,memory.used,memory.total --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadLine();
            process.WaitForExit(2000);
            if (string.IsNullOrWhiteSpace(output))
                return null;

            var values = output.Split(',', StringSplitOptions.TrimEntries);
            if (values.Length < 3 ||
                !long.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var usedMemory) ||
                !long.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalMemory))
                return null;

            return new NvidiaGpuStatus(values[0], usedMemory, totalMemory);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ConfigureBackend(string backend)
    {
        if (backendConfigured)
        {
            return;
        }

        switch (backend.Trim().ToUpperInvariant())
        {
            case "VULKAN":
                NativeLibraryConfig.All.WithCuda(false).WithVulkan().WithLogCallback(HandleNativeLog);
                break;
            case "CPU":
                NativeLibraryConfig.All.WithCuda(false).WithVulkan(false).WithLogCallback(HandleNativeLog);
                break;
            default:
                throw new ArgumentException("Backend must be Vulkan or CPU.", nameof(backend));
        }

        backendConfigured = true;
    }

    private void HandleNativeLog(LLamaLogLevel level, string message)
    {
        loadLog.Enqueue(message.TrimEnd());
    }

    public void Dispose()
    {
        weights?.Dispose();
        loadLock.Dispose();
    }
}

public sealed record ModelLoadStatus(
    string ModelPath,
    string Backend,
    int GpuLayerCount,
    ulong ModelSizeInBytes,
    string? GpuName,
    long? GpuUsedMemoryMiB,
    long? GpuTotalMemoryMiB,
    int FoundVulkanGpuCount,
    IReadOnlyList<VulkanDeviceStatus> VulkanDevices,
    string LoadLog);

internal sealed record NvidiaGpuStatus(string Name, long UsedMemoryMiB, long TotalMemoryMiB);
public sealed record VulkanDeviceStatus(string Name, int AssignedLayerCount, double? ModelBufferMiB);
