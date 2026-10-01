namespace Esi.AI.PyTorch;

/// <summary>Configures the local Python executable and durable adapter output directory.</summary>
public sealed class PyTorchTrainingOptions
{
    /// <summary>Gets or sets the Python executable name or absolute path.</summary>
    public string PythonExecutable { get; set; } = "python3";

    /// <summary>Gets or sets the root directory for adapter outputs.</summary>
    public string OutputDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Esi.AI",
        "PyTorch",
        "runs");

    /// <summary>Gets or sets an optional trainer script path override for isolated tests.</summary>
    public string? TrainerScriptPath { get; set; }
}