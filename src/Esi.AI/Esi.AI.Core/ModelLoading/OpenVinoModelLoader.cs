using OpenVinoSharp.GenAI;

namespace Esi.AI.Core.ModelLoading;

public sealed class OpenVinoModelLoader : IDisposable
{
    private readonly SemaphoreSlim loadLock = new(1, 1);
    private LLMPipeline? pipeline;
    private string? loadedModelPath;
    private string? loadedDevice;

    public bool IsLoaded => pipeline is not null;

    public string? LoadedModelPath => loadedModelPath;

    public string? LoadedDevice => loadedDevice;

    public OpenVinoModelLoadStatus GetStatus() => new(
        loadedModelPath,
        loadedDevice,
        IsLoaded);

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
        if (!isGgufFile && !Directory.Exists(fullModelPath))
        {
            if (File.Exists(fullModelPath))
                throw new ArgumentException("The model path must point to a .gguf file or an OpenVINO model directory.", nameof(modelPath));

            throw new FileNotFoundException($"The GGUF file or model directory was not found: {fullModelPath}", fullModelPath);
        }

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
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            GenAI.Initialize();
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
            var loadedPipeline = new LLMPipeline(fullModelPath, device, properties);
            try
            {
                using var generationConfig = new GenerationConfig();
                var options = generationOptions ?? new OpenVinoGenerationOptions();
                generationConfig.MaxNewTokens = (ulong)Math.Max(1, options.MaxNewTokens);
                generationConfig.SetTemperature(options.Temperature);
                generationConfig.SetTopP(options.TopP);
                generationConfig.SetDoSample(options.DoSample);
                loadedPipeline.SetGenerationConfig(generationConfig);

                pipeline?.Dispose();
                pipeline = loadedPipeline;
                loadedModelPath = fullModelPath;
                loadedDevice = device;
                loadedPipeline = null;
            }
            finally
            {
                loadedPipeline?.Dispose();
            }
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
            if (pipeline is null)
                throw new InvalidOperationException("Load an OpenVINO model before starting a chat.");

            return new OpenVinoChatSession(pipeline);
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
            pipeline?.Dispose();
            pipeline = null;
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
        pipeline?.Dispose();
        pipeline = null;
        loadLock.Dispose();
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
}

public sealed class OpenVinoChatSession : IDisposable
{
    private readonly LLMPipeline pipeline;

    internal OpenVinoChatSession(LLMPipeline pipeline)
    {
        this.pipeline = pipeline;
    }

    public string Generate(string prompt, Action<string>? streamer = null)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("A prompt is required.", nameof(prompt));

        using var results = streamer is null
            ? pipeline.Generate(prompt)
            : pipeline.Generate(prompt, streamer);
        return results.GetText();
    }

    public void Dispose()
    {
    }
}

public sealed record OpenVinoGenerationOptions(
    int MaxNewTokens = 512,
    float Temperature = .7f,
    float TopP = .9f,
    bool DoSample = true);

public sealed record OpenVinoNpuOptions(
    int MaxPromptLength = 1024,
    int MinResponseLength = 128,
    string PrefillHint = "DYNAMIC",
    string GenerateHint = "FAST_COMPILE");

public sealed record OpenVinoModelLoadStatus(
    string? ModelPath,
    string? Device,
    bool IsModelLoaded);
