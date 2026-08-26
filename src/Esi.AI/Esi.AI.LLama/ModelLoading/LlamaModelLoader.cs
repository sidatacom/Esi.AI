using System.Diagnostics;
using System.Globalization;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using LLama;
using LLama.Common;
using LLama.Native;
using Esi.AI.Llm.Chat;

namespace Esi.AI.Llm.ModelLoading;

public sealed class LlamaModelLoader : IDisposable
{
    private readonly SemaphoreSlim loadLock = new(1, 1);
    private readonly Dictionary<string, LoadedModel> loadedModels = new(StringComparer.OrdinalIgnoreCase);
    private LLamaWeights? weights;
    private bool backendConfigured;
    private readonly ConcurrentQueue<string> loadLog = new();
    private readonly ConcurrentDictionary<LlamaChatSession, byte> chatSessions = new();
    private CancellationTokenSource? activeLoadCancellation;

    public bool IsLoaded => weights is not null;

    public string? LoadedModelPath { get; private set; }

    public float Progress { get; private set; }

    public ModelLoadStatus? Status { get; private set; }

    public ModelLoadStatus GetStatus() => Status ?? CreateDiscoveryStatus();

    public LlamaChatSession CreateChatSession(string systemPrompt)
        => CreateChatSession(systemPrompt, LoadedModelPath);

    public LlamaChatSession CreateChatSession(string systemPrompt, string? modelPath)
    {
        loadLock.Wait();
        try
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                throw new InvalidOperationException("Load a model before starting a chat.");

            var normalizedModelPath = Path.GetFullPath(modelPath);
            if (!loadedModels.TryGetValue(normalizedModelPath, out var loadedModel))
                throw new InvalidOperationException("The selected model is not loaded.");

            var parameters = new ModelParams(normalizedModelPath)
            {
                ContextSize = loadedModel.Status.ContextSize
            };
            var context = loadedModel.Weights.CreateContext(parameters);
            LlamaChatSession? session = null;
            session = new LlamaChatSession(context, systemPrompt, () => chatSessions.TryRemove(session!, out _));
            chatSessions.TryAdd(session, 0);
            return session;
        }
        finally
        {
            loadLock.Release();
        }
    }

    public async Task LoadAsync(string modelPath, string backend, int gpuLayerCount, uint contextSize = (uint)LlamaContextSize.Context128K, IReadOnlyDictionary<string, float>? vulkanDeviceWeights = null, LlamaLoadOptions? advanced = null, CancellationToken cancellationToken = default)
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

        if (!Enum.IsDefined((LlamaContextSize)contextSize))
        {
            throw new ArgumentOutOfRangeException(nameof(contextSize), "The context size must be one of the supported values.");
        }

        ConfigureBackend(backend);
        while (loadLog.TryDequeue(out _))
        {
        }

        await loadLock.WaitAsync(cancellationToken);

        try
        {
            activeLoadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var loadCancellationToken = activeLoadCancellation.Token;
            Progress = 0;
            advanced ??= new();
            var parameters = new ModelParams(Path.GetFullPath(modelPath))
            {
                GpuLayerCount = gpuLayerCount,
                ContextSize = contextSize,
                SplitMode = GPUSplitMode.Layer,
                MainGpu = advanced.MainGpu,
                SeqMax = advanced.SeqMax,
                RecurrentRollbackSnapshots = advanced.RecurrentRollbackSnapshots,
                UseMemorymap = advanced.UseMemorymap,
                UseDirectIO = advanced.UseDirectIO,
                UseMemoryLock = advanced.UseMemoryLock,
                Threads = advanced.Threads,
                BatchThreads = advanced.BatchThreads,
                BatchSize = advanced.BatchSize,
                UBatchSize = advanced.UBatchSize,
                Embeddings = advanced.Embeddings,
                NoKqvOffload = advanced.NoKqvOffload,
                FlashAttention = advanced.FlashAttention,
                VocabOnly = advanced.VocabOnly,
                OpOffload = advanced.OpOffload,
                SwaFull = advanced.SwaFull,
                KVUnified = advanced.KVUnified,
                RopeFrequencyBase = advanced.RopeFrequencyBase,
                RopeFrequencyScale = advanced.RopeFrequencyScale,
                YarnExtrapolationFactor = advanced.YarnExtrapolationFactor,
                YarnAttentionFactor = advanced.YarnAttentionFactor,
                YarnBetaFast = advanced.YarnBetaFast,
                YarnBetaSlow = advanced.YarnBetaSlow,
                YarnOriginalContext = advanced.YarnOriginalContext
            };
            var deviceWeights = vulkanDeviceWeights ?? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var enabledDevices = deviceWeights
                .Where(device => device.Value > 0)
                .Select(device => device.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (string.Equals(backend, "VULKAN", StringComparison.OrdinalIgnoreCase))
            {
                parameters.TensorSplits.Clear();
                var splitIndex = 0;
                foreach (var device in deviceWeights
                    .Where(device => device.Value > 0)
                    .OrderBy(device => GetVulkanDeviceIndex(device.Key)))
                {
                    if (splitIndex >= parameters.TensorSplits.Length)
                        break;

                    parameters.TensorSplits[splitIndex++] = device.Value;
                }

                if (enabledDevices.Count == 0)
                {
                    parameters.GpuLayerCount = 0;
                }
            }
            LLamaWeights loadedWeights;
            try
            {
                loadedWeights = await LLamaWeights.LoadFromFileAsync(
                    parameters,
                    loadCancellationToken,
                    new Progress<float>(value => Progress = value));
            }
            catch
            {
                Status = CreateStatus(parameters.ModelPath, backend, parameters.GpuLayerCount, parameters.ContextSize ?? contextSize, 0, deviceWeights, false);
                throw;
            }

            if (loadedModels.Remove(parameters.ModelPath, out var previousModel))
                previousModel.Weights.Dispose();

            var loadedModel = new LoadedModel(loadedWeights, CreateStatus(parameters.ModelPath, backend, parameters.GpuLayerCount, parameters.ContextSize ?? contextSize, loadedWeights.SizeInBytes, deviceWeights, true));
            loadedModels[parameters.ModelPath] = loadedModel;
            weights = loadedWeights;
            LoadedModelPath = parameters.ModelPath;
            Status = loadedModel.Status with { LoadedModels = CreateLoadedModelStatuses() };
        }
        finally
        {
            activeLoadCancellation?.Dispose();
            activeLoadCancellation = null;
            loadLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        activeLoadCancellation?.Cancel();
        await loadLock.WaitAsync(cancellationToken);
        try
        {
            DisposeChatSessions();
            foreach (var model in loadedModels.Values)
                model.Weights.Dispose();
            loadedModels.Clear();
            weights = null;
            weights = null;
            LoadedModelPath = null;
            Progress = 0;
            while (loadLog.TryDequeue(out _))
            {
            }
            Status = CreateDiscoveryStatus();
        }
        finally
        {
            loadLock.Release();
        }
    }

    public async Task UnloadAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        await loadLock.WaitAsync(cancellationToken);
        try
        {
            if (!loadedModels.Remove(Path.GetFullPath(modelPath), out var model))
                return;

            model.Weights.Dispose();
            var current = loadedModels.Values.LastOrDefault();
            weights = current?.Weights;
            LoadedModelPath = current?.Status.ModelPath;
            Status = current is null
                ? CreateDiscoveryStatus()
                : current.Status with { LoadedModels = CreateLoadedModelStatuses() };
        }
        finally
        {
            loadLock.Release();
        }
    }

    private static int GetVulkanDeviceIndex(string deviceName) =>
        int.TryParse(deviceName.TrimStart("Vulkan".ToCharArray()), out var index) ? index : int.MaxValue;

    private ModelLoadStatus CreateStatus(string modelPath, string backend, int gpuLayerCount, uint contextSize, ulong modelSize, IReadOnlyDictionary<string, float> deviceWeights, bool isModelLoaded)
    {
        var nativeLog = string.Join(Environment.NewLine, loadLog);
        var vulkanDevices = MergeVulkanDevices(ParseVulkanDevices(nativeLog), CreateDiscoveryStatus().VulkanDevices);
        var cpuBufferMiB = ParseCpuBufferMiB(nativeLog);
        return new ModelLoadStatus(
            modelPath,
            backend.Trim().ToUpperInvariant(),
            gpuLayerCount,
            contextSize,
            modelSize,
            vulkanDevices.Count,
            vulkanDevices,
            cpuBufferMiB,
            nativeLog,
            deviceWeights.OrderBy(device => device.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(device => device.Key, device => device.Value, StringComparer.OrdinalIgnoreCase),
            isModelLoaded,
            CreateLoadedModelStatuses());
    }

    private IReadOnlyList<LoadedModelStatus> CreateLoadedModelStatuses() =>
        loadedModels.Values.Select(model => new LoadedModelStatus(
            model.Status.ModelPath!,
            model.Status.Backend,
            model.Status.GpuLayerCount,
            model.Status.ContextSize,
            model.Status.ModelSizeInBytes,
            model.Status.VulkanDevices,
            model.Status.CpuModelBufferMiB)).ToArray();

    private static IReadOnlyList<VulkanDeviceStatus> MergeVulkanDevices(
        IReadOnlyList<VulkanDeviceStatus> loadedDevices,
        IReadOnlyList<VulkanDeviceStatus> discoveredDevices)
    {
        return discoveredDevices
            .Concat(loadedDevices)
            .GroupBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<VulkanDeviceStatus> ParseVulkanDevices(string nativeLog)
    {
        var devices = new Dictionary<string, (int Layers, double? MemoryMiB, string? Description)>();
        foreach (Match match in Regex.Matches(nativeLog, @"(?:found|using)\s+(?:Vulkan\d+\s*:\s*)?(.+?)(?=\r?$)", RegexOptions.IgnoreCase | RegexOptions.Multiline))
        {
            var description = match.Groups[1].Value.Trim();
            if (description.Contains("Vulkan", StringComparison.OrdinalIgnoreCase))
            {
                var name = Regex.Match(description, @"\bVulkan\d+\b", RegexOptions.IgnoreCase).Value;
                if (!string.IsNullOrWhiteSpace(name))
                    devices[name] = (0, null, description);
            }
        }
        foreach (Match match in Regex.Matches(nativeLog, @"layer\s+\d+ assigned to device (Vulkan\d+)", RegexOptions.IgnoreCase))
        {
            var name = match.Groups[1].Value;
            devices.TryGetValue(name, out var status);
            devices[name] = (status.Layers + 1, status.MemoryMiB, status.Description);
        }

        foreach (Match match in Regex.Matches(nativeLog, @"(Vulkan\d+) model buffer size\s*=\s*([0-9.]+)\s*MiB", RegexOptions.IgnoreCase))
        {
            var name = match.Groups[1].Value;
            devices.TryGetValue(name, out var status);
            devices[name] = (status.Layers, double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), status.Description);
        }

        return devices
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new VulkanDeviceStatus(item.Key, item.Value.Description, item.Value.Layers, item.Value.MemoryMiB))
            .ToArray();
    }

    private static double? ParseCpuBufferMiB(string nativeLog)
    {
        var match = Regex.Match(nativeLog, @"CPU(?:_Mapped)? model buffer size\s*=\s*([0-9.]+)\s*MiB", RegexOptions.IgnoreCase);
        return match.Success ? double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    private static ModelLoadStatus CreateDiscoveryStatus()
    {
        var devices = new List<VulkanDeviceStatus>();
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "vulkaninfo",
                Arguments = "--summary",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is not null)
            {
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                process.WaitForExit(5000);
                var output = outputTask.GetAwaiter().GetResult();
                var error = errorTask.GetAwaiter().GetResult();
                var matches = Regex.Matches(output + Environment.NewLine + error, @"deviceName\s*=\s*(.+)", RegexOptions.IgnoreCase);
                var deviceIndex = 0;
                foreach (Match match in matches)
                {
                    var description = match.Groups[1].Value.Trim();
                    if (!description.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase))
                        devices.Add(new VulkanDeviceStatus($"Vulkan{deviceIndex}", description, 0, null));
                    deviceIndex++;
                }
            }
        }
        catch (Exception)
        {
        }

        return new ModelLoadStatus(
            null,
            "VULKAN",
            0,
            (uint)LlamaContextSize.Context128K,
            0,
            devices.Count,
            devices,
            null,
            string.Empty,
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase),
            false,
            []);
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
        activeLoadCancellation?.Cancel();
        DisposeChatSessions();
        foreach (var model in loadedModels.Values)
            model.Weights.Dispose();
        loadedModels.Clear();
        weights = null;
        activeLoadCancellation?.Dispose();
        loadLock.Dispose();
    }

    private void DisposeChatSessions()
    {
        foreach (var session in chatSessions.Keys)
            session.Dispose();
        chatSessions.Clear();
    }

    private sealed record LoadedModel(LLamaWeights Weights, ModelLoadStatus Status);
}

public sealed record ModelLoadStatus(
    string? ModelPath,
    string Backend,
    int GpuLayerCount,
    uint ContextSize,
    ulong ModelSizeInBytes,
    int FoundVulkanGpuCount,
    IReadOnlyList<VulkanDeviceStatus> VulkanDevices,
    double? CpuModelBufferMiB,
    string LoadLog,
    IReadOnlyDictionary<string, float> VulkanDeviceWeights,
    bool IsModelLoaded,
    IReadOnlyList<LoadedModelStatus> LoadedModels);

public sealed record LoadedModelStatus(
    string ModelPath,
    string Backend,
    int GpuLayerCount,
    uint ContextSize,
    ulong ModelSizeInBytes,
    IReadOnlyList<VulkanDeviceStatus> VulkanDevices,
    double? CpuModelBufferMiB);

public sealed record VulkanDeviceStatus(string Name, string? Description, int AssignedLayerCount, double? ModelBufferMiB);

public sealed record LlamaLoadOptions(
    int MainGpu = 0,
    uint SeqMax = 1,
    uint RecurrentRollbackSnapshots = 0,
    bool UseMemorymap = true,
    bool UseDirectIO = false,
    bool UseMemoryLock = false,
    int? Threads = null,
    int? BatchThreads = null,
    uint BatchSize = 512,
    uint UBatchSize = 512,
    bool Embeddings = false,
    bool NoKqvOffload = false,
    bool? FlashAttention = null,
    bool VocabOnly = false,
    bool? OpOffload = null,
    bool? SwaFull = null,
    bool? KVUnified = null,
    float? RopeFrequencyBase = null,
    float? RopeFrequencyScale = null,
    float? YarnExtrapolationFactor = null,
    float? YarnAttentionFactor = null,
    float? YarnBetaFast = null,
    float? YarnBetaSlow = null,
    uint? YarnOriginalContext = null);
