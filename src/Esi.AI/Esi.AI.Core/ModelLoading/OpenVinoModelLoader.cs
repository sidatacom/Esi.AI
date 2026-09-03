using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Esi.AI.Models;
using OpenVinoSharp;
using OpenVinoSharp.GenAI;
using OpenVinoSharp.Internal;

namespace Esi.AI.Core.ModelLoading;

public sealed class OpenVinoModelLoader : IDisposable
{
    private readonly SemaphoreSlim loadLock = new(1, 1);
    private readonly SemaphoreSlim generationLock = new(1, 1);
    private readonly ConcurrentQueue<string> loadLog = new();
    private static int linuxRuntimeInitialized;
    private LLMPipeline? llmPipeline;
    private VLMPipeline? vlmPipeline;
    private string? loadedModelPath;
    private string? loadedDevice;
    private double? vramUsageMiB;

    public OpenVinoModelLoader()
    {
        OvLogger.SetCallback(HandleLog);
    }

    public bool IsLoaded => llmPipeline is not null || vlmPipeline is not null;

    public string? LoadedModelPath => loadedModelPath;

    public string? LoadedDevice => loadedDevice;

    public OpenVinoModelLoadStatus GetStatus() => new(
        loadedModelPath,
        loadedDevice,
        IsLoaded,
        vramUsageMiB,
        string.Join(Environment.NewLine, loadLog));

    /// <summary>
    /// Configures the process to use the compatible OpenVINO runtime when one is available.
    /// </summary>
    /// <returns>The resolved native runtime directory, or <see langword="null"/> on non-Linux platforms.</returns>
    public static string? InitializeRuntime()
    {
        if (!OperatingSystem.IsLinux())
            return null;

        var runtimeDirectory = ResolveOpenVinoRuntimeDirectory();
        if (Interlocked.Exchange(ref linuxRuntimeInitialized, 1) == 0)
        {
            Environment.SetEnvironmentVariable("OPENVINO_RUNTIME_DIR", runtimeDirectory);
            Environment.SetEnvironmentVariable("OPENVINO_GENAI_RUNTIME_DIR", runtimeDirectory);

            var libraryPaths = new[]
            {
                runtimeDirectory,
                Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", "3rdparty", "tbb", "lib"))
            };
            var currentLibraryPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
            var configuredPaths = libraryPaths
                .Where(Directory.Exists)
                .ToArray();
            if (!string.IsNullOrWhiteSpace(currentLibraryPath))
                configuredPaths = configuredPaths.Append(currentLibraryPath).ToArray();
            Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", string.Join(Path.PathSeparator, configuredPaths));
            LoadLinuxOpenVinoDependencies(runtimeDirectory);
            var coreLibraryPath = ResolveNativeLibraryPath(runtimeDirectory, "libopenvino_c.so");
            if (coreLibraryPath is not null)
                OpenVinoSharp.Ov.Initialize(coreLibraryPath);
            NativeLibrary.SetDllImportResolver(
                typeof(OpenVinoSharp.Core).Assembly,
                static (libraryName, _, _) => ResolveOpenVinoNativeLibrary(libraryName));
        }

        return runtimeDirectory;
    }

    public async Task LoadAsync(
        string modelPath,
        string device = "GPU",
        CancellationToken cancellationToken = default,
        OpenVinoGenerationOptions? generationOptions = null,
        string? cacheDirectory = null,
        OpenVinoNpuOptions? npuOptions = null)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            throw new ArgumentException("A GGUF file or OpenVINO model directory is required.", nameof(modelPath));

        var fullModelPath = Path.GetFullPath(modelPath.Trim());
        var isGgufFile = File.Exists(fullModelPath) &&
            string.Equals(Path.GetExtension(fullModelPath), ".gguf", StringComparison.OrdinalIgnoreCase);
        var isVisionLanguageModel = !isGgufFile &&
            File.Exists(Path.Combine(fullModelPath, "openvino_vision_embeddings_model.xml"));
        if (!isGgufFile && !Directory.Exists(fullModelPath))
        {
            if (File.Exists(fullModelPath))
                throw new ArgumentException("The model path must point to a supported .gguf file or an OpenVINO model directory.", nameof(modelPath));

            throw new FileNotFoundException($"The GGUF file or OpenVINO model directory was not found: {fullModelPath}", fullModelPath);
        }

        if (isVisionLanguageModel)
            ValidateVisionLanguageModelCompatibility(fullModelPath);

        if (string.IsNullOrWhiteSpace(device))
            throw new ArgumentException("An OpenVINO device is required.", nameof(device));
        var isNpu = device.Equals("NPU", StringComparison.OrdinalIgnoreCase);
        var isGpu = device.StartsWith("GPU", StringComparison.OrdinalIgnoreCase) ||
            device.StartsWith("MULTI:GPU", StringComparison.OrdinalIgnoreCase);
        if (!isNpu && !isGpu)
            throw new ArgumentException("OpenVINO loading requires a GPU, MULTI:GPU, or NPU device route.", nameof(device));

        var npu = npuOptions ?? new OpenVinoNpuOptions();
        if (isNpu)
            ValidateNpuOptions(npu);

        cancellationToken.ThrowIfCancellationRequested();
        await loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var operation = "GenAI.Initialize";
        try
        {
            ClearLoadLog();
            AppendLoadLog($"Starting OpenVINO model load on {device}.");
            cancellationToken.ThrowIfCancellationRequested();
            var runtimeDirectory = InitializeRuntime();
            ConfigureVerboseLogging();
            OvLogger.Debug($"OpenVINO model load: path='{fullModelPath}', format={(isGgufFile ? "GGUF" : "OpenVINO IR directory")}, device='{device}'");

            operation = "GenAI.Initialize";
            var genAiLibraryPath = ResolveGenAiLibraryPath(runtimeDirectory);
            if (genAiLibraryPath is null)
                GenAI.Initialize();
            else
                GenAI.Initialize(genAiLibraryPath);
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(cacheDirectory))
                properties["CACHE_DIR"] = Path.GetFullPath(cacheDirectory.Trim());
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
                VLMPipeline? loadedPipeline = new VLMPipeline(fullModelPath, device, properties);
                try
                {
                    operation = "GenerationConfig.Configure";
                    using var generationConfig = loadedPipeline.GetGenerationConfig();
                    ConfigureGenerationConfig(generationConfig, generationOptions);
                    loadedPipeline.SetGenerationConfig(generationConfig);

                    DisposePipelines();
                    vlmPipeline = loadedPipeline;
                    loadedModelPath = fullModelPath;
                    loadedDevice = device;
                    loadedPipeline = null;
                }
                finally
                {
                    loadedPipeline?.Dispose();
                }
            }
            else
            {
                operation = "LLMPipeline.Create";
                LLMPipeline? loadedPipeline = new LLMPipeline(fullModelPath, device, properties);
                try
                {
                    operation = "GenerationConfig.Configure";
                    using var generationConfig = loadedPipeline.GetGenerationConfig();
                    ConfigureGenerationConfig(generationConfig, generationOptions);
                    loadedPipeline.SetGenerationConfig(generationConfig);

                    DisposePipelines();
                    llmPipeline = loadedPipeline;
                    loadedModelPath = fullModelPath;
                    loadedDevice = device;
                    loadedPipeline = null;
                }
                finally
                {
                    loadedPipeline?.Dispose();
                }
            }

            vramUsageMiB = TryGetVramUsageMiB(device);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OpenVinoModelLoadException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var loadException = new OpenVinoModelLoadException(fullModelPath, device, isGgufFile, operation, exception);
            OvLogger.Error(loadException.ToString());

            throw loadException;
        }
        finally
        {
            loadLock.Release();
        }
    }

    public OpenVinoChatSession CreateChatSession()
    {
        loadLock.Wait();
        try
        {
            if (llmPipeline is null && vlmPipeline is null)
                throw new InvalidOperationException("Load an OpenVINO model before starting a chat.");

            return llmPipeline is not null
                ? new OpenVinoChatSession(llmPipeline, generationLock)
                : new OpenVinoChatSession(vlmPipeline!, generationLock);
        }
        finally
        {
            loadLock.Release();
        }
    }

    public async Task UnloadAsync(CancellationToken cancellationToken = default)
    {
        await loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DisposePipelines();
            loadedModelPath = null;
            loadedDevice = null;
            vramUsageMiB = null;
        }
        finally
        {
            loadLock.Release();
        }
    }

    public void Dispose()
    {
        DisposePipelines();
        generationLock.Dispose();
        loadLock.Dispose();
    }

    private void HandleLog(LogLevel level, string message)
    {
        if (IsCoreLifecycleLog(message))
            return;

        AppendLoadLog($"[{level}] {message}");
    }

    private static bool IsCoreLifecycleLog(string message) =>
        message.Contains("OpenVINO Core", StringComparison.OrdinalIgnoreCase) &&
        (message.Contains("Creating OpenVINO Core instance", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("OpenVINO Core instance created successfully", StringComparison.OrdinalIgnoreCase));

    private void AppendLoadLog(string message)
    {
        loadLog.Enqueue(message);
        while (loadLog.Count > 4000)
            loadLog.TryDequeue(out _);
    }

    private double? TryGetVramUsageMiB(string device)
    {
        try
        {
            using var core = new OpenVinoSharp.Core();
            var devices = device.StartsWith("MULTI:", StringComparison.OrdinalIgnoreCase)
                ? device["MULTI:".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [device];
            var memoryBytes = devices
                .SelectMany(selectedDevice => ParseMemoryStatistics(core.GetProperty(selectedDevice, "GPU_MEMORY_STATISTICS")))
                .Sum();
            if (memoryBytes <= 0)
            {
                AppendLoadLog($"[INFO] OpenVINO VRAM statistics are unavailable for {device}.");
                return null;
            }

            var memoryMiB = memoryBytes / 1024d / 1024d;
            AppendLoadLog($"[INFO] OpenVINO VRAM allocated on {device}: {memoryMiB:F2} MiB.");
            return memoryMiB;
        }
        catch (Exception exception)
        {
            AppendLoadLog($"[DEBUG] OpenVINO VRAM statistics unavailable for {device}: {exception.Message}");
            return null;
        }
    }

    private static IEnumerable<long> ParseMemoryStatistics(string statistics)
    {
        var entries = Regex.Matches(
                statistics,
                @"(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*[:=]\s*(?<value>\d+(?:\.\d+)?)\s*(?<unit>GiB|MiB|KiB|B)?",
                RegexOptions.IgnoreCase)
            .Select(match =>
            {
                var value = double.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture);
                var unit = match.Groups["unit"].Value;
                var multiplier = unit.Equals("GiB", StringComparison.OrdinalIgnoreCase) ? 1024d * 1024d * 1024d :
                    unit.Equals("MiB", StringComparison.OrdinalIgnoreCase) ? 1024d * 1024d :
                    unit.Equals("KiB", StringComparison.OrdinalIgnoreCase) ? 1024d : 1d;
                return value * multiplier;
            })
            .Select(value => checked((long)value));

        return entries.Any() ? entries :
            long.TryParse(statistics.Trim(), out var value) ? [value] : [];
    }

    private void ClearLoadLog()
    {
        while (loadLog.TryDequeue(out _))
        {
        }
    }

    private static void ConfigureGenerationConfig(GenerationConfig generationConfig, OpenVinoGenerationOptions? generationOptions)
    {
        var options = generationOptions ?? new OpenVinoGenerationOptions();
        generationConfig.MaxNewTokens = (ulong)Math.Max(1, options.MaxNewTokens);
        generationConfig.SetTemperature(options.Temperature);
        generationConfig.SetTopK((ulong)Math.Max(1, options.TopK));
        generationConfig.SetTopP(options.TopP);
        generationConfig.SetDoSample(options.DoSample);
        generationConfig.SetRepetitionPenalty(options.RepetitionPenalty);
    }

    private void DisposePipelines()
    {
        llmPipeline?.Dispose();
        llmPipeline = null;
        vlmPipeline?.Dispose();
        vlmPipeline = null;
    }

    private static void ValidateNpuOptions(OpenVinoNpuOptions options)
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

        if (!IsOpenVinoGenAi2026_4OrLater(ResolveOpenVinoRuntimeDirectory()))
        {
            throw new NotSupportedException(
                "Qwen3.8 VLM models require OpenVINO GenAI 2026.4 or later. Set OPENVINO_RUNTIME_DIR to a compatible runtime before loading this model.");
        }
    }

    private static void LoadLinuxOpenVinoDependencies(string runtimeDirectory)
    {
        var tbbLibrary = Path.GetFullPath(Path.Combine(
            runtimeDirectory,
            "..",
            "..",
            "3rdparty",
            "tbb",
            "lib",
            "libtbb.so.12"));
        if (File.Exists(tbbLibrary))
            NativeLibrary.Load(tbbLibrary);

        var libraries = new[]
        {
            "libopenvino.so.2640",
            "libopenvino_c.so.2640",
            "libopenvino_genai.so.2640"
        };
        foreach (var library in libraries)
        {
            var path = Path.Combine(runtimeDirectory, library);
            if (File.Exists(path))
                NativeLibrary.Load(path);
        }
    }

    private static string? ResolveGenAiLibraryPath(string? runtimeDirectory)
    {
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
            return null;

        var libraryNames = new[]
        {
            "libopenvino_genai_c.so",
            "libopenvino_genai_c.so.2640",
            "libopenvino_genai_c.so.2026.4.0.0"
        };
        return libraryNames
            .Select(library => Path.Combine(runtimeDirectory, library))
            .FirstOrDefault(File.Exists);
    }

    private static IntPtr ResolveOpenVinoNativeLibrary(string libraryName)
    {
        var runtimeDirectory = ResolveOpenVinoRuntimeDirectory();
        var libraryPrefix = libraryName switch
        {
            "openvino_c" => "libopenvino_c.so",
            "openvino_genai_c" => "libopenvino_genai_c.so",
            _ => null
        };
        if (libraryPrefix is null)
            return IntPtr.Zero;

        var libraryPath = ResolveNativeLibraryPath(runtimeDirectory, libraryPrefix);
        return libraryPath is null ? IntPtr.Zero : NativeLibrary.Load(libraryPath);
    }

    private static string? ResolveNativeLibraryPath(string runtimeDirectory, string libraryPrefix) => new[]
    {
        Path.Combine(runtimeDirectory, libraryPrefix),
        Path.Combine(runtimeDirectory, $"{libraryPrefix}.2640"),
        Path.Combine(runtimeDirectory, $"{libraryPrefix}.2630"),
        Path.Combine(runtimeDirectory, $"{libraryPrefix}.2026.4.0.0"),
        Path.Combine(runtimeDirectory, $"{libraryPrefix}.2026.3.0.0")
    }.FirstOrDefault(File.Exists);

    private static string ResolveOpenVinoRuntimeDirectory()
    {
        var configuredRuntimeDirectory = Environment.GetEnvironmentVariable("OPENVINO_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(configuredRuntimeDirectory))
        {
            var configuredPath = Path.GetFullPath(configuredRuntimeDirectory.Trim());
            return File.Exists(Path.Combine(configuredPath, "libopenvino_genai.so"))
                ? configuredPath
                : Path.Combine(configuredPath, "runtime", "lib", "intel64");
        }

        var cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache",
            "esi-ai",
            "openvino");
        if (Directory.Exists(cacheRoot))
        {
            var cachedRuntime = Directory.EnumerateFiles(cacheRoot, "libopenvino_genai.so.2640", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (cachedRuntime is not null)
                return cachedRuntime;
        }

        return Path.Combine(AppContext.BaseDirectory, "runtimes", "linux-x64", "native");
    }

    private static bool IsOpenVinoGenAi2026_4OrLater(string runtimeDirectory) =>
        File.Exists(Path.Combine(runtimeDirectory, "libopenvino_genai.so.2640")) ||
        File.Exists(Path.Combine(runtimeDirectory, "libopenvino_genai.so.2026.4.0.0"));

    private static void ConfigureVerboseLogging()
    {
        OvLogger.MinLevel = LogLevel.DEBUG;
        OvLogger.EnableNativeCallback();
    }
}

/// <summary>
/// Describes a failure while creating or configuring an OpenVINO model pipeline.
/// </summary>
public sealed class OpenVinoModelLoadException : Exception
{
    /// <summary>
    /// Initializes a model-load exception with the failed model and device context.
    /// </summary>
    public OpenVinoModelLoadException(
        string modelPath,
        string device,
        bool isGgufFile,
        string operation,
        Exception innerException)
        : base(CreateMessage(modelPath, device, isGgufFile, operation, innerException), innerException)
    {
        ModelPath = modelPath;
        Device = device;
        ModelFormat = isGgufFile ? "GGUF" : "OpenVINO IR directory";
        Operation = operation;
        NativeStatusCode = innerException switch
        {
            GenAIException genAiStatusException => genAiStatusException.StatusCode,
            OVException openVinoStatusException => (int)openVinoStatusException.Status,
            _ => (int?)null
        };
        NativeOperation = innerException is GenAIException genAiOperationException
            ? genAiOperationException.Operation
            : operation;
    }

    /// <summary>
    /// Gets the normalized path of the model that failed to load.
    /// </summary>
    public string ModelPath { get; }

    /// <summary>
    /// Gets the OpenVINO device route used for loading.
    /// </summary>
    public string Device { get; }

    /// <summary>
    /// Gets the detected model format.
    /// </summary>
    public string ModelFormat { get; }

    /// <summary>
    /// Gets the Esi.AI loading operation that failed.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// Gets the native GenAI status code, when the inner exception provides one.
    /// </summary>
    public int? NativeStatusCode { get; }

    /// <summary>
    /// Gets the native GenAI operation, when the inner exception provides one.
    /// </summary>
    public string? NativeOperation { get; }

    private static string CreateMessage(string modelPath, string device, bool isGgufFile, string operation, Exception exception)
    {
        var format = isGgufFile ? "GGUF file" : "OpenVINO IR model directory";
        var nativeDetails = exception switch
        {
            GenAIException genAiStatusException => $" Native operation '{genAiStatusException.Operation}' returned status {genAiStatusException.StatusCode}: {genAiStatusException.Message}.",
            OVException openVinoStatusException => $" OpenVINO operation '{operation}' returned status {(int)openVinoStatusException.Status}: {openVinoStatusException.Message}.",
            _ => $" {exception.Message}"
        };
        var guidance = isGgufFile
            ? " Direct GGUF loading depends on architecture support in the installed OpenVINO GenAI runtime; use a converted OpenVINO IR model directory when this GGUF is unsupported."
            : " Verify that the directory contains the OpenVINO model, tokenizer, detokenizer, and generation configuration files.";

        return $"Failed during '{operation}' while loading {format} '{modelPath}' on OpenVINO device '{device}'.{nativeDetails}{guidance}";
    }
}

public sealed class OpenVinoChatSession : IDisposable
{
    private static readonly JsonSerializerOptions ChatJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LLMPipeline? llmPipeline;
    private readonly VLMPipeline? vlmPipeline;
    private readonly SemaphoreSlim generationLock;

    internal OpenVinoChatSession(LLMPipeline pipeline, SemaphoreSlim generationLock)
    {
        llmPipeline = pipeline;
        this.generationLock = generationLock;
    }

    internal OpenVinoChatSession(VLMPipeline pipeline, SemaphoreSlim generationLock)
    {
        vlmPipeline = pipeline;
        this.generationLock = generationLock;
    }

    public string Generate(string prompt, Action<string>? streamer = null, OpenVinoGenerationOptions? generationOptions = null) => GenerateWithStats(prompt, streamer, generationOptions).Text;

    public OpenVinoGenerationResult GenerateWithStats(string prompt, Action<string>? streamer = null, OpenVinoGenerationOptions? generationOptions = null)
    {
        return GenerateWithStats(
            [new OpenAiChatMessage("user", prompt)],
            null,
            streamer,
            generationOptions);
    }

    public OpenVinoGenerationResult GenerateWithStats(
        IReadOnlyList<OpenAiChatMessage> messages,
        IReadOnlyList<OpenAiToolDefinition>? tools = null,
        Action<string>? streamer = null,
        OpenVinoGenerationOptions? generationOptions = null)
    {
        if (messages is null || messages.Count == 0)
            throw new ArgumentException("At least one chat message is required.", nameof(messages));

        generationLock.Wait();
        try
        {
            using var generationConfig = GetGenerationConfig(generationOptions);
            using var history = CreateChatHistory(messages, tools, generationOptions?.ReasoningEffort);
            if (llmPipeline is not null)
            {
                using var results = streamer is null
                    ? llmPipeline.GenerateWithHistory(history, generationConfig)
                    : llmPipeline.GenerateWithHistory(history, generationConfig, text =>
                    {
                        streamer(text);
                        return StreamingStatus.Running;
                    });
                return CreateGenerationResult(results.GetText(), results.GetPerformanceMetrics());
            }

            if (vlmPipeline is not null)
            {
                if (streamer is null)
                {
                    using var nonStreamingResults = vlmPipeline.GenerateWithHistory(history, null, generationConfig);
                    return CreateGenerationResult(nonStreamingResults.GetText(), nonStreamingResults.GetPerformanceMetrics());
                }

                using var streamedResults = vlmPipeline.GenerateWithHistory(history, null, generationConfig, text =>
                {
                    streamer(text);
                    return StreamingStatus.Running;
                });
                return CreateGenerationResult(streamedResults.GetText(), streamedResults.GetPerformanceMetrics());
            }

            throw new InvalidOperationException("No OpenVINO pipeline is available for this chat session.");
        }
        finally
        {
            generationLock.Release();
        }
    }

    private static ChatHistory CreateChatHistory(
        IReadOnlyList<OpenAiChatMessage> messages,
        IReadOnlyList<OpenAiToolDefinition>? tools,
        string? reasoningEffort)
    {
        var history = new ChatHistory();
        foreach (var message in messages)
            history.PushBackJson(SerializeChatMessageForHistory(message));

        var templateContext = CreateChatTemplateContext(reasoningEffort);
        if (templateContext is not null)
        {
            using var context = JsonContainer.FromJsonString(templateContext);
            history.SetExtraContext(context);
        }

        if (tools is { Count: > 0 })
        {
            using var toolDefinitions = JsonContainer.FromJsonString(JsonSerializer.Serialize(tools, ChatJsonOptions));
            history.SetTools(toolDefinitions);
        }

        return history;
    }

    internal static string? CreateChatTemplateContext(string? reasoningEffort)
    {
        if (string.IsNullOrWhiteSpace(reasoningEffort))
            return null;

        var normalizedEffort = reasoningEffort.Trim().ToLowerInvariant();
        if (normalizedEffort == "none")
            return "{\"enable_thinking\":false}";

        var templateEffort = normalizedEffort switch
        {
            "low" or "medium" or "xhigh" => normalizedEffort,
            "high" or "max" => "xhigh",
            _ => throw new ArgumentException($"Unsupported reasoning effort '{reasoningEffort}'.", nameof(reasoningEffort))
        };
        return JsonSerializer.Serialize(new
        {
            enable_thinking = true,
            reasoning_effort = templateEffort
        }, ChatJsonOptions);
    }

    internal static string SerializeChatMessageForHistory(OpenAiChatMessage message)
    {
        var json = JsonSerializer.SerializeToNode(message, ChatJsonOptions)?.AsObject()
            ?? throw new InvalidOperationException("The chat message could not be serialized.");

        if (message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) &&
            json["tool_calls"] is JsonArray toolCalls)
        {
            if (message.Content is null)
                json["content"] = string.Empty;

            foreach (var toolCall in toolCalls)
            {
                if (toolCall?["function"] is not JsonObject function ||
                    function["arguments"] is not JsonValue arguments ||
                    !arguments.TryGetValue<string>(out var argumentsText))
                    continue;

                try
                {
                    if (JsonNode.Parse(argumentsText) is JsonObject argumentsObject)
                        function["arguments"] = argumentsObject;
                }
                catch (JsonException)
                {
                }
            }
        }

        return json.ToJsonString(ChatJsonOptions);
    }

    private static OpenVinoGenerationResult CreateGenerationResult(string text, PerformanceMetrics metrics)
    {
        using (metrics)
        {
            var parsed = ParseToolCalls(text);
            return new OpenVinoGenerationResult(
                parsed.Text,
                checked((int)metrics.NumGenerationTokens),
                metrics.Throughput.Mean,
                checked((int)metrics.NumInputTokens),
                parsed.ToolCalls,
                parsed.ToolCalls.Count > 0 ? "tool_calls" : "stop");
        }
    }

    internal static OpenVinoToolCallParseResult ParseToolCalls(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new OpenVinoToolCallParseResult(string.Empty, []);

        var toolCalls = new List<OpenAiToolCall>();
        var visibleText = new System.Text.StringBuilder(text.Length);
        var position = 0;
        foreach (Match block in ToolCallBlockRegex.Matches(text))
        {
            visibleText.Append(text, position, block.Index - position);
            var parsedCalls = ParseToolCallBlock(block.Groups["body"].Value, toolCalls.Count);
            if (parsedCalls.Count == 0)
                visibleText.Append(block.Value);
            else
                toolCalls.AddRange(parsedCalls);
            position = block.Index + block.Length;
        }

        visibleText.Append(text, position, text.Length - position);
        return new OpenVinoToolCallParseResult(visibleText.ToString().Trim(), toolCalls);
    }

    private static IReadOnlyList<OpenAiToolCall> ParseToolCallBlock(string body, int index)
    {
        var functionMatch = FunctionCallRegex.Match(body);
        if (functionMatch.Success)
        {
            var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (Match parameter in ParameterRegex.Matches(functionMatch.Groups["body"].Value))
            {
                var value = parameter.Groups["value"].Value.Trim();
                arguments[parameter.Groups["name"].Value] = ParseToolParameter(value);
            }

            return [new OpenAiToolCall(
                $"call_{Guid.NewGuid():N}",
                "function",
                new OpenAiToolCallFunction(
                    functionMatch.Groups["name"].Value,
                    JsonSerializer.Serialize(arguments, ChatJsonOptions)))];
        }

        try
        {
            using var document = JsonDocument.Parse(body.Trim());
            var root = document.RootElement;
            if (!root.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("arguments", out var arguments))
                return [];

            var argumentsText = arguments.ValueKind == JsonValueKind.String
                ? arguments.GetString() ?? "{}"
                : arguments.GetRawText();
            return [new OpenAiToolCall(
                $"call_{Guid.NewGuid():N}",
                "function",
                new OpenAiToolCallFunction(name.GetString()!, argumentsText))];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static JsonElement ParseToolParameter(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, ChatJsonOptions));
            return document.RootElement.Clone();
        }
    }

    private static readonly Regex ToolCallBlockRegex = new(
        @"<tool_call>(?<body>.*?)</tool_call>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex FunctionCallRegex = new(
        @"<function=(?<name>[^>\s]+)>(?<body>.*?)</function>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex ParameterRegex = new(
        @"<parameter=(?<name>[^>\s]+)>(?<value>.*?)</parameter>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private GenerationConfig GetGenerationConfig(OpenVinoGenerationOptions? options)
    {
        var generationConfig = llmPipeline?.GetGenerationConfig() ?? vlmPipeline?.GetGenerationConfig();
        if (generationConfig is null)
            throw new InvalidOperationException("No OpenVINO pipeline is available for this chat session.");
        ConfigureGenerationConfig(generationConfig, options);
        return generationConfig;
    }

    private static void ConfigureGenerationConfig(GenerationConfig generationConfig, OpenVinoGenerationOptions? options)
    {
        var value = options ?? new OpenVinoGenerationOptions();
        generationConfig.SetMaxNewTokens((ulong)Math.Max(1, value.MaxNewTokens));
        generationConfig.SetTemperature(value.Temperature);
        generationConfig.SetTopK((ulong)Math.Max(1, value.TopK));
        generationConfig.SetTopP(value.TopP);
        generationConfig.SetDoSample(value.DoSample);
        generationConfig.SetRepetitionPenalty(value.RepetitionPenalty);
        generationConfig.SetPresencePenalty(value.PresencePenalty);
        generationConfig.SetFrequencyPenalty(value.FrequencyPenalty);
        if (value.Seed is int seed)
            generationConfig.SetRngSeed((ulong)seed);
        if (value.StopSequences is { Count: > 0 })
            generationConfig.SetStopStrings(value.StopSequences);
    }

    public void Dispose()
    {
    }
}

public sealed record OpenVinoGenerationResult(
    string Text,
    int TokenCount,
    double TokensPerSecond,
    int PromptTokenCount = 0,
    IReadOnlyList<OpenAiToolCall>? ToolCalls = null,
    string FinishReason = "stop");

internal sealed record OpenVinoToolCallParseResult(string Text, IReadOnlyList<OpenAiToolCall> ToolCalls);

public sealed record OpenVinoGenerationOptions(
    int MaxNewTokens = 128,
    float Temperature = .7f,
    float TopP = .9f,
    bool DoSample = true,
    int TopK = 50,
    float RepetitionPenalty = 1.2f,
    float FrequencyPenalty = 0,
    float PresencePenalty = 0,
    int? Seed = null,
    IReadOnlyList<string>? StopSequences = null,
    string? ReasoningEffort = null);

public sealed record OpenVinoNpuOptions(
    int MaxPromptLength = 1024,
    int MinResponseLength = 128,
    string PrefillHint = "DYNAMIC",
    string GenerateHint = "FAST_COMPILE");

public sealed record OpenVinoModelLoadStatus(
    string? ModelPath,
    string? Device,
    bool IsModelLoaded,
    double? VramUsageMiB,
    string LoadLog);
