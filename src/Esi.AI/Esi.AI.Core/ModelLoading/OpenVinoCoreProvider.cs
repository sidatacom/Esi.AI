using OpenVinoSharp;

namespace Esi.AI.Core.ModelLoading;

/// <summary>Owns the process-wide OpenVINO Core used for device diagnostics and memory telemetry.</summary>
public sealed class OpenVinoCoreProvider : IDisposable
{
    private readonly Lazy<OpenVinoSharp.Core> core = new(CreateCore, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Gets the shared OpenVINO Core, initializing the native runtime on first use.</summary>
    public OpenVinoSharp.Core Core => core.Value;

    /// <summary>Releases the shared native Core when the application host shuts down.</summary>
    public void Dispose()
    {
        if (core.IsValueCreated)
            core.Value.Dispose();
    }

    private static OpenVinoSharp.Core CreateCore()
    {
        OpenVinoModelLoader.InitializeRuntime();
        return new OpenVinoSharp.Core();
    }
}
