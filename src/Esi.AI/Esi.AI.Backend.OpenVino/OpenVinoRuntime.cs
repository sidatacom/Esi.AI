using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using Esi.AI.Backend.Abstractions;
using Esi.AI.Models;
using OpenVinoSharp;
using OpenVinoSharp.GenAI;
using OpenVinoSharp.Internal;

namespace Esi.AI.Backend.OpenVino;

/// <summary>Loads OpenVINO models and serves normalized chat generation requests.</summary>
public sealed class OpenVinoRuntime : IBackendRuntime
{
    private const string VariantId = "openvino";
    private static readonly JsonSerializerOptions ConfigurationJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly object NativeInitializationLock = new();
    private static readonly List<IntPtr> NativeLibraryHandles = [];
    private static bool linuxRuntimeInitialized;
    private readonly SemaphoreSlim loadLock = new(1, 1);
    private readonly SemaphoreSlim generationLock = new(1, 1);
    private readonly ConcurrentQueue<string> loadLog = new();
    private LLMPipeline? llmPipeline;
    private VLMPipeline? vlmPipeline;
    private string? loadedModelPath;
    private string? loadedDevice;
    private int disposed;

    /// <summary>Creates a runtime and connects OpenVINO native log output to its status log.</summary>
    public OpenVinoRuntime()
    {
        OvLogger.SetCallback(HandleLog);
    }

    /// <inheritdoc />
    public BackendVariantDescriptor Descriptor { get; } = new(
        VariantId,
        ConfigurationBackend.OpenVino,
        "OpenVINO",
        "OpenVINO",
        "OpenVINO");

    /// <inheritdoc />
    public ModelLoadStatus GetStatus()
    {
        var isLoaded = loadedModelPath is not null && (llmPipeline is not null || vlmPipeline is not null);
        var modelSize = isLoaded ? GetModelSize(loadedModelPath!) : 0;
        var log = string.Join(Environment.NewLine, loadLog);
        var loadedModels = isLoaded
            ? new[]
            {
                new LoadedModelStatus(
                    loadedModelPath!,
                    ConfigurationBackend.OpenVino,
                    Descriptor.RuntimeName,
                    0,
                    0,
                    modelSize,
                    [],
                    null,
                    log,
                    BackendVariantId: Descriptor.Id)
            }
            : [];

        return new ModelLoadStatus(
            loadedModelPath,
            loadedDevice ?? Descriptor.Route,
            0,
            0,
            modelSize,
            0,
            [],
            null,
            log,
            new Dictionary<string, float>(),
            isLoaded,
            loadedModels,
            Descriptor.Id);
    }

    /// <inheritdoc />
    public bool SupportsImageInput(string? modelPath) =>
        vlmPipeline is not null &&
        (string.IsNullOrWhiteSpace(modelPath) || string.Equals(
            Path.GetFullPath(modelPath), loadedModelPath, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public async Task LoadAsync(BackendLoadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        if (!string.Equals(request.VariantId, VariantId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"This runtime supports only '{VariantId}'.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ModelPath))
            throw new ArgumentException("A model path is required.", nameof(request));

        var configuration = request.Configuration.Deserialize<OpenVinoLoadRequest>(ConfigurationJsonOptions)
            ?? throw new ArgumentException("An OpenVINO model configuration is required.", nameof(request));
        var modelPath = Path.GetFullPath(request.ModelPath.Trim());
        var isGgufFile = File.Exists(modelPath) && string.Equals(Path.GetExtension(modelPath), ".gguf", StringComparison.OrdinalIgnoreCase);
        var isVisionLanguageModel = !isGgufFile && File.Exists(Path.Combine(modelPath, "openvino_vision_embeddings_model.xml"));
        if (!isGgufFile && !Directory.Exists(modelPath))
        {
            if (File.Exists(modelPath))
                throw new ArgumentException("The model path must point to a supported .gguf file or an OpenVINO model directory.", nameof(request));
            throw new FileNotFoundException($"The GGUF file or OpenVINO model directory was not found: {modelPath}", modelPath);
        }

        if (string.IsNullOrWhiteSpace(configuration.Device))
            throw new ArgumentException("An OpenVINO device is required.", nameof(request));
        var isNpu = configuration.Device.Equals("NPU", StringComparison.OrdinalIgnoreCase);
        var isGpu = configuration.Device.StartsWith("GPU", StringComparison.OrdinalIgnoreCase) ||
            configuration.Device.StartsWith("MULTI:GPU", StringComparison.OrdinalIgnoreCase);
        if (!isNpu && !isGpu)
            throw new ArgumentException("OpenVINO loading requires a GPU, MULTI:GPU, or NPU device route.", nameof(request));

        var npu = configuration.Npu ?? new OpenVinoNpuSettings();
        if (isNpu)
            ValidateNpuOptions(npu);
        cancellationToken.ThrowIfCancellationRequested();

        await loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var generationLockHeld = false;
        var operation = "GenAI.Initialize";
        try
        {
            await generationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            generationLockHeld = true;
            ClearLoadLog();
            DisposePipelines();
            ClearLoadedModelState();
            AppendLoadLog($"Starting OpenVINO model load on {configuration.Device}.");
            cancellationToken.ThrowIfCancellationRequested();

            if (isVisionLanguageModel)
                ValidateVisionLanguageModelCompatibility(modelPath);

            var runtimeDirectory = InitializeRuntime();
            ConfigureVerboseLogging();
            var genAiLibraryPath = ResolveGenAiLibraryPath(runtimeDirectory);
            if (genAiLibraryPath is null)
                GenAI.Initialize();
            else
                GenAI.Initialize(genAiLibraryPath);

            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(configuration.CacheDirectory))
                properties["CACHE_DIR"] = Path.GetFullPath(configuration.CacheDirectory.Trim());
            if (TryGetDynamicQuantizationGroupSize(modelPath) is int groupSize)
                properties["DYNAMIC_QUANTIZATION_GROUP_SIZE"] = groupSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (isVisionLanguageModel && IsQwen35Model(modelPath))
                properties["ATTENTION_BACKEND"] = "SDPA";
            if (isNpu)
            {
                properties["MAX_PROMPT_LEN"] = npu.MaxPromptLength.ToString(System.Globalization.CultureInfo.InvariantCulture);
                properties["MIN_RESPONSE_LEN"] = npu.MinResponseLength.ToString(System.Globalization.CultureInfo.InvariantCulture);
                properties["PREFILL_HINT"] = npu.PrefillHint;
                properties["GENERATE_HINT"] = npu.GenerateHint;
            }

            if (isVisionLanguageModel)
            {
                operation = "VLMPipeline.Create";
                VLMPipeline? pipeline = new VLMPipeline(modelPath, configuration.Device, properties);
                try
                {
                    using var generationConfig = pipeline.GetGenerationConfig();
                    ConfigureGenerationConfig(generationConfig, configuration);
                    pipeline.SetGenerationConfig(generationConfig);
                    cancellationToken.ThrowIfCancellationRequested();
                    vlmPipeline = pipeline;
                    pipeline = null;
                }
                finally
                {
                    pipeline?.Dispose();
                }
            }
            else
            {
                operation = "LLMPipeline.Create";
                LLMPipeline? pipeline = new LLMPipeline(modelPath, configuration.Device, properties);
                try
                {
                    using var generationConfig = pipeline.GetGenerationConfig();
                    ConfigureGenerationConfig(generationConfig, configuration);
                    pipeline.SetGenerationConfig(generationConfig);
                    cancellationToken.ThrowIfCancellationRequested();
                    llmPipeline = pipeline;
                    pipeline = null;
                }
                finally
                {
                    pipeline?.Dispose();
                }
            }

            loadedModelPath = modelPath;
            loadedDevice = configuration.Device;
        }
        catch (OperationCanceledException)
        {
            DisposePipelines();
            ClearLoadedModelState();
            throw;
        }
        catch (Exception exception)
        {
            DisposePipelines();
            ClearLoadedModelState();
            throw new InvalidOperationException(
                $"Failed during '{operation}' while loading OpenVINO model '{modelPath}' on '{configuration.Device}': {exception.Message}",
                exception);
        }
        finally
        {
            if (generationLockHeld)
                generationLock.Release();
            loadLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task UnloadAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ThrowIfDisposed();
        await loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var generationLockHeld = false;
        try
        {
            await generationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            generationLockHeld = true;
            if (!string.Equals(Path.GetFullPath(modelPath), loadedModelPath, StringComparison.OrdinalIgnoreCase))
                return;
            DisposePipelines();
            ClearLoadedModelState();
        }
        finally
        {
            if (generationLockHeld)
                generationLock.Release();
            loadLock.Release();
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default) => UnloadLoadedModelAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<GenerationResult> GenerateAsync(
        OpenAiBackendChatRequest request,
        Func<string, Task>? onToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        if (request.Messages.Count == 0 && request.StructuredMessages.Count == 0)
            throw new ArgumentException("At least one chat message is required.", nameof(request));

        await loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await generationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            loadLock.Release();
        }

        var images = Array.Empty<Tensor>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pipelineSupportsImages = vlmPipeline is not null;
            if (!pipelineSupportsImages && request.Messages.Any(message => message.Images is { Count: > 0 }))
                throw new NotSupportedException("Image input requires an OpenVINO vision-language model.");
            images = OpenVinoImageTensorFactory.Create(request.Messages);

            var chatMessages = request.StructuredMessages.Count > 0
                ? request.StructuredMessages
                : request.Messages.Select(message => new OpenAiChatMessage(
                    message.Role,
                    message.Content,
                    message.ToolCalls,
                    message.ToolCallId)).ToArray();
            var chatSession = llmPipeline is not null
                ? new OpenVinoChatSession(llmPipeline)
                : vlmPipeline is not null
                    ? new OpenVinoChatSession(vlmPipeline)
                    : throw new InvalidOperationException("Load an OpenVINO model before starting a chat.");
            var tools = request.Tools ?? request.Options.Tools;
            var options = new OpenVinoGenerationOptions(
                request.Options.MaxTokens,
                request.Options.Temperature,
                request.Options.TopP,
                true,
                request.Options.TopK,
                request.Options.RepetitionPenalty,
                request.Options.FrequencyPenalty,
                request.Options.PresencePenalty,
                request.Options.Seed,
                request.Options.StopSequences,
                request.Options.ReasoningEffort);
            var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
            var generationTask = Task.Run(() =>
            {
                try
                {
                    return chatSession.Generate(chatMessages, tools, text => channel.Writer.TryWrite(text), options, images);
                }
                finally
                {
                    channel.Writer.TryComplete();
                }
            }, CancellationToken.None);

            Exception? callbackException = null;
            await foreach (var token in channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (onToken is null || cancellationToken.IsCancellationRequested || callbackException is not null)
                    continue;
                try
                {
                    await onToken(token).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    callbackException = exception;
                }
            }

            var result = await generationTask.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (callbackException is not null)
                ExceptionDispatchInfo.Capture(callbackException).Throw();

            var durationMs = Math.Max(0, (result.PrefillDurationMs ?? 0) + (result.DecodeDurationMs ?? 0));
            return new GenerationResult(
                result.Text,
                result.TokenCount,
                TimeSpan.FromMilliseconds(durationMs),
                result.TokensPerSecond,
                result.PromptTokenCount,
                result.FinishReason,
                result.ToolCalls,
                result.TimeToFirstTokenMs,
                result.PrefillDurationMs,
                result.DecodeDurationMs);
        }
        finally
        {
            foreach (var image in images)
                image.Dispose();
            generationLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        UnloadLoadedModelAsync(CancellationToken.None).GetAwaiter().GetResult();
        generationLock.Dispose();
        loadLock.Dispose();
    }

    private async Task UnloadLoadedModelAsync(CancellationToken cancellationToken)
    {
        await loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var generationLockHeld = false;
        try
        {
            await generationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            generationLockHeld = true;
            DisposePipelines();
            ClearLoadedModelState();
        }
        finally
        {
            if (generationLockHeld)
                generationLock.Release();
            loadLock.Release();
        }
    }

    internal static string? InitializeRuntime()
    {
        if (!OperatingSystem.IsLinux())
            return null;

        var runtimeDirectory = ResolveOpenVinoRuntimeDirectory();
        lock (NativeInitializationLock)
        {
            if (linuxRuntimeInitialized)
                return runtimeDirectory;

            Environment.SetEnvironmentVariable("OPENVINO_RUNTIME_DIR", runtimeDirectory);
            Environment.SetEnvironmentVariable("OPENVINO_GENAI_RUNTIME_DIR", runtimeDirectory);
            var tbbDirectory = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", "3rdparty", "tbb", "lib"));
            var libraryPaths = new[] { runtimeDirectory, tbbDirectory }.Where(Directory.Exists).ToList();
            var existingPaths = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
            if (!string.IsNullOrWhiteSpace(existingPaths))
                libraryPaths.Add(existingPaths);
            Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", string.Join(Path.PathSeparator, libraryPaths));

            var tbbPath = new[]
            {
                Path.Combine(runtimeDirectory, "libtbb.so.12"),
                Path.Combine(tbbDirectory, "libtbb.so.12")
            }.FirstOrDefault(File.Exists);
            if (tbbPath is not null)
                NativeLibraryHandles.Add(NativeLibrary.Load(tbbPath));

            var corePath = ResolveNativeLibraryPath(runtimeDirectory, "libopenvino.so");
            if (corePath is not null)
                NativeLibraryHandles.Add(NativeLibrary.Load(corePath));

            var coreLibraryPath = ResolveNativeLibraryPath(runtimeDirectory, "libopenvino_c.so");
            if (coreLibraryPath is not null)
                OpenVinoSharp.Ov.Initialize(coreLibraryPath);
            var genAiLibraryPath = ResolveNativeLibraryPath(runtimeDirectory, "libopenvino_genai.so");
            if (genAiLibraryPath is not null)
                NativeLibraryHandles.Add(NativeLibrary.Load(genAiLibraryPath));
            NativeLibrary.SetDllImportResolver(
                typeof(OpenVinoSharp.Core).Assembly,
                static (libraryName, _, _) => ResolveOpenVinoNativeLibrary(libraryName));
            linuxRuntimeInitialized = true;
        }

        return runtimeDirectory;
    }

    private static string ResolveOpenVinoRuntimeDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("OPENVINO_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var path = Path.GetFullPath(configured.Trim());
            if (File.Exists(Path.Combine(path, "libopenvino_genai.so")) || Directory.EnumerateFiles(path, "libopenvino_genai.so*").Any())
                return path;
            var nestedRuntime = Path.Combine(path, "runtime", "lib", "intel64");
            if (Directory.Exists(nestedRuntime))
                return nestedRuntime;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "runtimes", "linux-x64", "native"),
            AppContext.BaseDirectory
        };
        return candidates.FirstOrDefault(path => Directory.Exists(path) &&
            Directory.EnumerateFiles(path, "libopenvino_genai.so*").Any()) ?? candidates[0];
    }

    private static string? ResolveGenAiLibraryPath(string? runtimeDirectory) => string.IsNullOrWhiteSpace(runtimeDirectory)
        ? null
        : ResolveNativeLibraryPath(runtimeDirectory, "libopenvino_genai_c.so");

    private static string? ResolveNativeLibraryPath(string runtimeDirectory, string libraryPrefix) => new[]
    {
        Path.Combine(runtimeDirectory, libraryPrefix),
        Path.Combine(runtimeDirectory, $"{libraryPrefix}.2631"),
        Path.Combine(runtimeDirectory, $"{libraryPrefix}.2640"),
        Path.Combine(runtimeDirectory, $"{libraryPrefix}.2630"),
        Path.Combine(runtimeDirectory, $"{libraryPrefix}.2026.3.1"),
        Path.Combine(runtimeDirectory, $"{libraryPrefix}.2026.3.1.0"),
        Path.Combine(runtimeDirectory, $"{libraryPrefix}.2026.4.0.0"),
        Path.Combine(runtimeDirectory, $"{libraryPrefix}.2026.3.0.0")
    }.FirstOrDefault(File.Exists);

    private static IntPtr ResolveOpenVinoNativeLibrary(string libraryName)
    {
        var prefix = libraryName switch
        {
            "openvino_c" => "libopenvino_c.so",
            "openvino_genai_c" => "libopenvino_genai_c.so",
            _ => null
        };
        if (prefix is null)
            return IntPtr.Zero;
        var path = ResolveNativeLibraryPath(ResolveOpenVinoRuntimeDirectory(), prefix);
        return path is null ? IntPtr.Zero : NativeLibrary.Load(path);
    }

    private static void ValidateNpuOptions(OpenVinoNpuSettings options)
    {
        if (options.MaxPromptLength < 1)
            throw new ArgumentOutOfRangeException(nameof(options.MaxPromptLength), "MAX_PROMPT_LEN must be greater than zero.");
        if (options.MinResponseLength < 1)
            throw new ArgumentOutOfRangeException(nameof(options.MinResponseLength), "MIN_RESPONSE_LEN must be greater than zero.");
        if (!options.PrefillHint.Equals("DYNAMIC", StringComparison.OrdinalIgnoreCase) &&
            !options.PrefillHint.Equals("STATIC", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("PREFILL_HINT must be DYNAMIC or STATIC.", nameof(options));
        if (!options.GenerateHint.Equals("FAST_COMPILE", StringComparison.OrdinalIgnoreCase) &&
            !options.GenerateHint.Equals("BEST_PERF", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("GENERATE_HINT must be FAST_COMPILE or BEST_PERF.", nameof(options));
    }

    private static void ValidateVisionLanguageModelCompatibility(string modelPath)
    {
        var configPath = Path.Combine(modelPath, "config.json");
        if (!File.Exists(configPath))
            return;
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        if (!document.RootElement.TryGetProperty("model_type", out var modelType) ||
            !string.Equals(modelType.GetString(), "qwen3_5", StringComparison.OrdinalIgnoreCase))
            return;
        var runtimeDirectory = ResolveOpenVinoRuntimeDirectory();
        if (!File.Exists(Path.Combine(runtimeDirectory, "libopenvino_genai.so.2640")) &&
            !File.Exists(Path.Combine(runtimeDirectory, "libopenvino_genai.so.2026.4.0.0")) &&
            !File.Exists(Path.Combine(runtimeDirectory, "libopenvino_genai.so")))
            throw new NotSupportedException("Qwen3.8 VLM models require OpenVINO GenAI 2026.4 or later.");
    }

    private static bool IsQwen35Model(string modelPath)
    {
        var configPath = Path.Combine(modelPath, "config.json");
        if (!File.Exists(configPath))
            return false;
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        return document.RootElement.TryGetProperty("model_type", out var modelType) &&
            string.Equals(modelType.GetString(), "qwen3_5", StringComparison.OrdinalIgnoreCase);
    }

    private static int? TryGetDynamicQuantizationGroupSize(string modelPath)
    {
        var configPath = Path.Combine(modelPath, "openvino_config.json");
        if (!File.Exists(configPath))
            return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!document.RootElement.TryGetProperty("quantization_config", out var quantizationConfig) ||
                !quantizationConfig.TryGetProperty("quantization_configs", out var quantizationConfigs) ||
                !quantizationConfigs.TryGetProperty("lm_model", out var languageModelConfig) ||
                !languageModelConfig.TryGetProperty("dq_group_size", out var groupSize) ||
                !groupSize.TryGetInt32(out var value) || value <= 0)
                return null;
            return value;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ConfigureGenerationConfig(GenerationConfig config, OpenVinoLoadRequest options)
    {
        config.SetMaxNewTokens((ulong)Math.Max(1, options.MaxNewTokens));
        config.SetTemperature(options.Temperature);
        config.SetTopK((ulong)Math.Max(1, options.TopK));
        config.SetTopP(options.TopP);
        config.SetDoSample(options.DoSample);
        config.SetRepetitionPenalty(options.RepetitionPenalty);
    }

    private static void ConfigureVerboseLogging()
    {
        OvLogger.MinLevel = LogLevel.DEBUG;
        OvLogger.EnableNativeCallback();
    }

    private static ulong GetModelSize(string modelPath)
    {
        if (File.Exists(modelPath))
            return (ulong)new FileInfo(modelPath).Length;
        return Directory.Exists(modelPath)
            ? (ulong)Directory.EnumerateFiles(modelPath, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length)
            : 0;
    }

    private void HandleLog(LogLevel level, string message)
    {
        if (message.Contains("OpenVINO Core", StringComparison.OrdinalIgnoreCase) &&
            (message.Contains("Creating OpenVINO Core instance", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("OpenVINO Core instance created successfully", StringComparison.OrdinalIgnoreCase)))
            return;
        AppendLoadLog($"[{level}] {message}");
    }

    private void AppendLoadLog(string message)
    {
        loadLog.Enqueue(message);
        while (loadLog.Count > 4000)
            loadLog.TryDequeue(out _);
    }

    private void ClearLoadLog()
    {
        while (loadLog.TryDequeue(out _))
        {
        }
    }

    private void DisposePipelines()
    {
        llmPipeline?.Dispose();
        llmPipeline = null;
        vlmPipeline?.Dispose();
        vlmPipeline = null;
    }

    private void ClearLoadedModelState()
    {
        loadedModelPath = null;
        loadedDevice = null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
}