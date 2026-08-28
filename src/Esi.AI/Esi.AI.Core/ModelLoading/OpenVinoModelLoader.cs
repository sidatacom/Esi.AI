using System.Runtime.InteropServices;
using System.Text.Json;
using OpenVinoSharp;
using OpenVinoSharp.GenAI;
using OpenVinoSharp.Internal;

namespace Esi.AI.Core.ModelLoading;

public sealed class OpenVinoModelLoader : IDisposable
{
    private readonly SemaphoreSlim loadLock = new(1, 1);
    private static int linuxRuntimeInitialized;
    private LLMPipeline? llmPipeline;
    private VLMPipeline? vlmPipeline;
    private string? loadedModelPath;
    private string? loadedDevice;

    public bool IsLoaded => llmPipeline is not null || vlmPipeline is not null;

    public string? LoadedModelPath => loadedModelPath;

    public string? LoadedDevice => loadedDevice;

    public OpenVinoModelLoadStatus GetStatus() => new(
        loadedModelPath,
        loadedDevice,
        IsLoaded);

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
            cancellationToken.ThrowIfCancellationRequested();
            var runtimeDirectory = InitializeRuntime();
            ConfigureVerboseLogging();
            if (IsVerboseLoggingEnabled())
            {
                OvLogger.Debug($"OpenVINO model load: path='{fullModelPath}', format={(isGgufFile ? "GGUF" : "OpenVINO IR directory")}, device='{device}'");
            }

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
            if (IsVerboseLoggingEnabled())
            {
                OvLogger.Error(loadException.ToString());
            }

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
                ? new OpenVinoChatSession(llmPipeline)
                : new OpenVinoChatSession(vlmPipeline!);
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
        }
        finally
        {
            loadLock.Release();
        }
    }

    public void Dispose()
    {
        DisposePipelines();
        loadLock.Dispose();
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
        if (!IsVerboseLoggingEnabled())
            return;

        Environment.SetEnvironmentVariable("OPENVINO_LOG_LEVEL", "DEBUG");
        OvLogger.MinLevel = LogLevel.DEBUG;
        OvLogger.EnableNativeCallback();
    }

    private static bool IsVerboseLoggingEnabled() => string.Equals(
        Environment.GetEnvironmentVariable("ESI_OPENVINO_VERBOSE"),
        "1",
        StringComparison.Ordinal);
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
    private readonly LLMPipeline? llmPipeline;
    private readonly VLMPipeline? vlmPipeline;

    internal OpenVinoChatSession(LLMPipeline pipeline)
    {
        llmPipeline = pipeline;
    }

    internal OpenVinoChatSession(VLMPipeline pipeline)
    {
        vlmPipeline = pipeline;
    }

    public string Generate(string prompt, Action<string>? streamer = null) => GenerateWithStats(prompt, streamer).Text;

    public OpenVinoGenerationResult GenerateWithStats(string prompt, Action<string>? streamer = null)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("A prompt is required.", nameof(prompt));

        if (llmPipeline is not null)
        {
            using var results = streamer is null
                ? llmPipeline.Generate(prompt)
                : llmPipeline.Generate(prompt, streamer);
            return CreateGenerationResult(results.GetText(), results.GetPerformanceMetrics());
        }

        if (vlmPipeline is not null)
        {
            if (streamer is null)
            {
                using var results = vlmPipeline.Generate(prompt);
                return CreateGenerationResult(results.GetText(), results.GetPerformanceMetrics());
            }

            using var streamedResults = vlmPipeline.Generate(prompt, text =>
            {
                streamer(text);
                return StreamingStatus.Running;
            });
            return CreateGenerationResult(streamedResults.GetText(), streamedResults.GetPerformanceMetrics());
        }

        throw new InvalidOperationException("No OpenVINO pipeline is available for this chat session.");
    }

    private static OpenVinoGenerationResult CreateGenerationResult(string text, PerformanceMetrics metrics)
    {
        using (metrics)
        {
            return new OpenVinoGenerationResult(text, checked((int)metrics.NumGenerationTokens), metrics.Throughput.Mean);
        }
    }

    public void Dispose()
    {
    }
}

public sealed record OpenVinoGenerationResult(string Text, int TokenCount, double TokensPerSecond);

public sealed record OpenVinoGenerationOptions(
    int MaxNewTokens = 512,
    float Temperature = .7f,
    float TopP = .9f,
    bool DoSample = true,
    int TopK = 50,
    float RepetitionPenalty = 1.2f);

public sealed record OpenVinoNpuOptions(
    int MaxPromptLength = 1024,
    int MinResponseLength = 128,
    string PrefillHint = "DYNAMIC",
    string GenerateHint = "FAST_COMPILE");

public sealed record OpenVinoModelLoadStatus(
    string? ModelPath,
    string? Device,
    bool IsModelLoaded);
