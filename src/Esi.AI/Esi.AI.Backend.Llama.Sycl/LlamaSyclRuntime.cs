using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Esi.AI.Backend.Abstractions;
using Esi.AI.Models;
using LLama;
using LLama.Abstractions;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;

namespace Esi.AI.Backend.Llama.Sycl;

/// <summary>Loads GGUF models and serves normalized chat requests through LLamaSharp's SYCL runtime.</summary>
public sealed class LlamaSyclRuntime : IBackendRuntime
{
    private const string VariantId = "llama.sycl";
    private static readonly object NativeConfigurationLock = new();
    private static readonly ConcurrentQueue<string> NativeLog = new();
    private static readonly JsonSerializerOptions ConfigurationJsonOptions = new(JsonSerializerDefaults.Web);
    private static bool nativeConfigured;
    private readonly SemaphoreSlim runtimeLock = new(1, 1);
    private readonly string applicationDirectory;
    private LLamaWeights? weights;
    private string? loadedModelPath;
    private uint contextSize = 131072;
    private int disposed;

    /// <summary>Initializes a SYCL runtime using the application runtime directory.</summary>
    /// <param name="applicationDirectory">An optional directory containing the packaged SYCL runtime.</param>
    public LlamaSyclRuntime(string? applicationDirectory = null)
    {
        this.applicationDirectory = Path.GetFullPath(applicationDirectory ?? AppContext.BaseDirectory);
    }

    /// <inheritdoc />
    public BackendVariantDescriptor Descriptor { get; } = new(
        VariantId,
        ConfigurationBackend.Llama,
        "Llama.cpp SYCL",
        "LLamaSharp SYCL",
        "SYCL");

    /// <inheritdoc />
    public ModelLoadStatus GetStatus()
    {
        var devices = Volatile.Read(ref nativeConfigured) ? GetSyclDevices() : [];
        var isLoaded = weights is not null && loadedModelPath is not null;
        var modelSize = isLoaded && File.Exists(loadedModelPath) ? (ulong)new FileInfo(loadedModelPath).Length : 0;
        var log = string.Join(Environment.NewLine, NativeLog);
        var loadedModels = isLoaded
            ? new[]
            {
                new LoadedModelStatus(
                    loadedModelPath!,
                    ConfigurationBackend.Llama,
                    Descriptor.RuntimeName,
                    -1,
                    contextSize,
                    modelSize,
                    devices,
                    null,
                    log,
                    BackendVariantId: Descriptor.Id)
            }
            : [];

        return new ModelLoadStatus(
            loadedModelPath,
            Descriptor.Route,
            -1,
            contextSize,
            modelSize,
            devices.Count,
            devices,
            null,
            log,
            new Dictionary<string, float>(),
            isLoaded,
            loadedModels,
            Descriptor.Id);
    }

    /// <inheritdoc />
    public bool SupportsImageInput(string? modelPath) => false;

    /// <inheritdoc />
    public async Task LoadAsync(BackendLoadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        if (!string.Equals(request.VariantId, VariantId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"This runtime supports only '{VariantId}'.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ModelPath))
            throw new ArgumentException("A model path is required.", nameof(request));
        if (!File.Exists(request.ModelPath))
            throw new FileNotFoundException("The model file was not found.", request.ModelPath);
        if (!string.Equals(Path.GetExtension(request.ModelPath), ".gguf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The model file must use the .gguf extension.", nameof(request));

        var configuration = request.Configuration.Deserialize<LoadModelRequest>(ConfigurationJsonOptions)
            ?? throw new ArgumentException("A SYCL model configuration is required.", nameof(request));
        if (configuration.GpuLayerCount < -1)
            throw new ArgumentOutOfRangeException(nameof(request), "GPU layers must be -1 (all layers) or greater.");

        var runtimeDirectory = SyclRuntimeFiles.GetRuntimeDirectory(applicationDirectory);
        SyclRuntimeFiles.Validate(runtimeDirectory);
        SyclRuntimeFiles.PrepareLibraryPath(runtimeDirectory);
        SyclRuntimeFiles.PrepareSyclRuntimeEnvironment(runtimeDirectory);
        ConfigureNativeBackend(runtimeDirectory);

        await runtimeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (NativeLog.TryDequeue(out _))
            {
            }

            var advanced = configuration.Advanced;
            var modelParams = new ModelParams(Path.GetFullPath(request.ModelPath))
            {
                GpuLayerCount = -1,
                ContextSize = configuration.ContextSize,
                SplitMode = GPUSplitMode.Layer,
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
                YarnOriginalContext = advanced.YarnOriginalContext,
                Devices = ResolveSyclDevices(configuration.Devices)
            };

            var newWeights = await LLamaWeights.LoadFromFileAsync(
                modelParams,
                cancellationToken,
                new Progress<float>(_ => { })).ConfigureAwait(false);
            weights?.Dispose();
            weights = newWeights;
            loadedModelPath = modelParams.ModelPath;
            contextSize = configuration.ContextSize;
        }
        finally
        {
            runtimeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task UnloadAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        await runtimeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.Equals(Path.GetFullPath(modelPath), loadedModelPath, StringComparison.OrdinalIgnoreCase))
                return;

            weights?.Dispose();
            weights = null;
            loadedModelPath = null;
        }
        finally
        {
            runtimeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await runtimeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            weights?.Dispose();
            weights = null;
            loadedModelPath = null;
            while (NativeLog.TryDequeue(out _))
            {
            }
        }
        finally
        {
            runtimeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<GenerationResult> GenerateAsync(
        OpenAiBackendChatRequest request,
        Func<string, Task>? onToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        if (request.Messages.Count == 0)
            throw new ArgumentException("At least one chat message is required.", nameof(request));

        await runtimeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var currentWeights = weights ?? throw new InvalidOperationException("Load a model before starting a chat.");
            var messages = request.Messages;
            using var context = currentWeights.CreateContext(new ModelParams(loadedModelPath!) { ContextSize = contextSize });
            var session = new ChatSession(new InteractiveExecutor(context))
            {
                HistoryTransform = new ChatMlHistoryTransform()
            };

            for (var index = 0; index < messages.Count - 1; index++)
                session.AddMessage(new ChatHistory.Message(ParseRole(messages[index].Role), messages[index].Content));

            var resultText = new StringBuilder();
            var stopwatch = Stopwatch.StartNew();
            double? firstTokenMs = null;
            var lastMessage = messages[^1];
            await foreach (var token in session.ChatAsync(
                new ChatHistory.Message(ParseRole(lastMessage.Role), lastMessage.Content),
                new InferenceParams
                {
                    MaxTokens = request.Options.MaxTokens,
                    AntiPrompts = ["<|im_end|>", "\nUser:", .. request.Options.StopSequences ?? []],
                    SamplingPipeline = new DefaultSamplingPipeline
                    {
                        Temperature = request.Options.Temperature,
                        TopP = request.Options.TopP,
                        TopK = request.Options.TopK,
                        MinP = request.Options.MinP,
                        RepeatPenalty = request.Options.RepetitionPenalty,
                        FrequencyPenalty = request.Options.FrequencyPenalty,
                        PresencePenalty = request.Options.PresencePenalty,
                        PenaltyCount = request.Options.PenaltyCount,
                        Seed = (uint)(request.Options.Seed ?? Random.Shared.Next())
                    }
                },
                cancellationToken).ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(token) && firstTokenMs is null)
                    firstTokenMs = stopwatch.Elapsed.TotalMilliseconds;
                resultText.Append(token);
                if (onToken is not null)
                    await onToken(token).ConfigureAwait(false);
            }

            stopwatch.Stop();
            var text = CleanGeneratedText(resultText.ToString());
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("The model returned an empty answer.");

            var tokenCount = context.Tokenize(text, addBos: false, special: true).Length;
            var promptTokenCount = context.Tokenize(CreatePrompt(messages), addBos: true, special: true).Length;
            var tokensPerSecond = stopwatch.Elapsed.TotalSeconds > 0 ? tokenCount / stopwatch.Elapsed.TotalSeconds : 0;
            double? decodeDurationMs = firstTokenMs is double first ? Math.Max(0, stopwatch.Elapsed.TotalMilliseconds - first) : null;
            return new GenerationResult(text, tokenCount, stopwatch.Elapsed, tokensPerSecond, promptTokenCount,
                TimeToFirstTokenMs: firstTokenMs, PrefillDurationMs: firstTokenMs, DecodeDurationMs: decodeDurationMs);
        }
        finally
        {
            runtimeLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        weights?.Dispose();
        weights = null;
        runtimeLock.Dispose();
    }

    private static void ConfigureNativeBackend(string runtimeDirectory)
    {
        lock (NativeConfigurationLock)
        {
            if (nativeConfigured)
                return;

            NativeLibraryConfig.All
                .WithSearchDirectory(runtimeDirectory)
                .WithCuda(false)
                .WithVulkan(false)
                .WithSycl(true)
                .WithAutoFallback(false)
                .WithLogCallback((_, message) => NativeLog.Enqueue(message.TrimEnd()));
            nativeConfigured = true;
        }
    }

    private static IReadOnlyList<string> ResolveSyclDevices(IReadOnlyList<string>? requestedDevices)
    {
        if (requestedDevices is not { Count: > 0 })
            return [];

        var availableDevices = Enumerable.Range(0, checked((int)NativeApi.ggml_backend_dev_count()))
            .Select(index => Marshal.PtrToStringAnsi(NativeApi.ggml_backend_dev_name(NativeApi.ggml_backend_dev_get((nuint)index))))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unavailable = requestedDevices.Where(device => !availableDevices.Contains(device)).ToArray();
        if (unavailable.Length > 0)
            throw new ArgumentException($"SYCL device(s) were not found: {string.Join(", ", unavailable)}.", nameof(requestedDevices));

        return requestedDevices;
    }

    private static IReadOnlyList<DeviceStatus> GetSyclDevices()
    {
        try
        {
            var devices = new List<DeviceStatus>();
            for (nuint index = 0; index < NativeApi.ggml_backend_dev_count(); index++)
            {
                var device = NativeApi.ggml_backend_dev_get(index);
                var name = Marshal.PtrToStringAnsi(NativeApi.ggml_backend_dev_name(device));
                if (string.IsNullOrWhiteSpace(name) || !name.StartsWith("SYCL", StringComparison.OrdinalIgnoreCase))
                    continue;

                devices.Add(new DeviceStatus(name, Marshal.PtrToStringAnsi(NativeApi.ggml_backend_dev_description(device)), 0, null, "Intel", "SYCL native runtime"));
            }

            return devices;
        }
        catch
        {
            return [];
        }
    }

    private static AuthorRole ParseRole(string role) => role.ToLowerInvariant() switch
    {
        "system" => AuthorRole.System,
        "assistant" => AuthorRole.Assistant,
        "user" => AuthorRole.User,
        _ => throw new ArgumentException($"Unsupported chat role '{role}'.", nameof(role))
    };

    private static string CleanGeneratedText(string text)
    {
        var cleaned = text.Trim();
        var endMarker = cleaned.IndexOf("<|im_end|>", StringComparison.OrdinalIgnoreCase);
        if (endMarker >= 0)
            cleaned = cleaned[..endMarker].TrimEnd();
        var userMarker = cleaned.IndexOf("\nUser:", StringComparison.OrdinalIgnoreCase);
        return userMarker >= 0 ? cleaned[..userMarker].TrimEnd() : cleaned;
    }

    private static string CreatePrompt(IReadOnlyList<ChatMessage> messages) =>
        string.Concat(messages.Select(message => $"<|im_start|>{message.Role}\n{message.Content}<|im_end|>\n")) + "<|im_start|>assistant\n";

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

    private sealed class ChatMlHistoryTransform : IHistoryTransform
    {
        public string HistoryToText(ChatHistory history) =>
            string.Concat(history.Messages.Select(message => $"<|im_start|>{GetRoleName(message.AuthorRole)}\n{message.Content}<|im_end|>\n")) + "<|im_start|>assistant\n";

        public ChatHistory TextToHistory(AuthorRole role, string text)
        {
            var history = new ChatHistory();
            history.AddMessage(role, text);
            return history;
        }

        public IHistoryTransform Clone() => new ChatMlHistoryTransform();

        private static string GetRoleName(AuthorRole role) => role switch
        {
            AuthorRole.System => "system",
            AuthorRole.User => "user",
            AuthorRole.Assistant => "assistant",
            _ => "user"
        };
    }
}

internal static class SyclRuntimeFiles
{
    private static readonly string[] RequiredFiles =
    [
        "libggml-base.so",
        "libggml.so",
        "libggml-sycl.so",
        "libimf.so",
        "libirng.so",
        "libllama.so",
        "libmtmd.so",
        "libsvml.so",
        "libur_adapter_level_zero.so",
        "libze_loader.so"
    ];

    internal static string GetRuntimeDirectory(string applicationDirectory)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The LLama SYCL backend package currently supports Linux x64 only.");

        return Path.Combine(applicationDirectory, "runtimes", "linux-x64", "native", "sycl");
    }

    internal static void Validate(string runtimeDirectory)
    {
        var missing = RequiredFiles.Where(file => !File.Exists(Path.Combine(runtimeDirectory, file))).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"The LLama SYCL runtime is incomplete. Missing: {string.Join(", ", missing)} in '{runtimeDirectory}'.");
    }

    internal static void PrepareLibraryPath(string runtimeDirectory)
    {
        if (!OperatingSystem.IsLinux() || !Directory.Exists(runtimeDirectory))
            return;

        var currentPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        if (currentPath?.Split(Path.PathSeparator).Contains(runtimeDirectory, StringComparer.Ordinal) == true)
            return;

        Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", string.IsNullOrWhiteSpace(currentPath)
            ? runtimeDirectory
            : string.Join(Path.PathSeparator, runtimeDirectory, currentPath));
    }

    internal static void PrepareSyclRuntimeEnvironment(string runtimeDirectory)
    {
        if (!OperatingSystem.IsLinux())
            return;

        var bundledLoader = Path.Combine(runtimeDirectory, "libze_loader.so.1");
        var bundledDriver = Path.Combine(runtimeDirectory, "libze_intel_gpu.so.1");
        var bundledAdapter = Path.Combine(runtimeDirectory, "libur_adapter_level_zero.so");
        if (File.Exists(bundledLoader) && File.Exists(bundledDriver) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ZE_ENABLE_ALT_DRIVERS")))
            Environment.SetEnvironmentVariable("ZE_ENABLE_ALT_DRIVERS", bundledDriver);
        if (File.Exists(bundledLoader) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ZES_ENABLE_SYSMAN")))
            Environment.SetEnvironmentVariable("ZES_ENABLE_SYSMAN", "1");
        if (File.Exists(bundledAdapter) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UR_ADAPTERS_FORCE_LOAD")))
            Environment.SetEnvironmentVariable("UR_ADAPTERS_FORCE_LOAD", bundledAdapter);

        Environment.SetEnvironmentVariable("ONEAPI_DEVICE_SELECTOR", "level_zero:gpu");
    }
}