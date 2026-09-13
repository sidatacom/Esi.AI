namespace Esi.AI.Core.ModelLoading;

/// <summary>Resolves the isolated runtime directory assigned to each backend.</summary>
public static class BackendRuntimePaths
{
    /// <summary>Returns the native LLama directory for a backend route.</summary>
    public static string GetLlamaDirectory(string backend, string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backend);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);

        var route = backend.Trim().ToLowerInvariant() switch
        {
            "cuda" or "cuda12" => "cuda12",
            "sycl" or "sycl16" or "xpu" => "sycl",
            "vulkan" => "vulkan",
            "cpu" => "cpu",
            _ => throw new ArgumentException($"Unsupported LLama backend route '{backend}'.", nameof(backend))
        };

        return Path.Combine(Path.GetFullPath(applicationDirectory), "runtimes", "linux-x64", "native", route);
    }

    /// <summary>Returns the isolated Python environment root.</summary>
    public static string GetPythonRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("ESI_PYTHON_ENV_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredRoot.Trim()));

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(
            string.IsNullOrWhiteSpace(userProfile) ? AppContext.BaseDirectory : userProfile,
            ".venvs");
    }

    /// <summary>Returns the application-local OpenVINO directory when it contains a runtime.</summary>
    public static string GetOpenVinoDirectory(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);

        var localDirectory = Path.Combine(Path.GetFullPath(applicationDirectory), "runtimes", "linux-x64", "native", "openvino");
        return localDirectory;
    }

    /// <summary>Adds one backend directory to the native loader search path.</summary>
    public static void PrependLibraryPath(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory) || !OperatingSystem.IsLinux())
            return;

        var currentPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        if (currentPath?.Split(Path.PathSeparator).Contains(directory, StringComparer.Ordinal) == true)
            return;

        var paths = string.IsNullOrWhiteSpace(currentPath)
            ? [directory]
            : new[] { directory, currentPath };
        Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", string.Join(Path.PathSeparator, paths));
    }

    /// <summary>Sets the bundled Level Zero and Unified Runtime selectors before native runtimes initialize.</summary>
    public static void PrepareSyclRuntimeEnvironment(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        if (!OperatingSystem.IsLinux())
            return;

        var runtimeDirectory = GetLlamaDirectory("SYCL", applicationDirectory);
        var bundledLoader = Path.Combine(runtimeDirectory, "libze_loader.so.1");
        var bundledDriver = Path.Combine(runtimeDirectory, "libze_intel_gpu.so.1");
        var bundledAdapter = Path.Combine(runtimeDirectory, "libur_adapter_level_zero_v2.so.0");
        if (File.Exists(bundledLoader) && File.Exists(bundledDriver) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ZE_ENABLE_ALT_DRIVERS")))
            Environment.SetEnvironmentVariable("ZE_ENABLE_ALT_DRIVERS", bundledDriver);

        if (File.Exists(bundledLoader) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ZES_ENABLE_SYSMAN")))
            Environment.SetEnvironmentVariable("ZES_ENABLE_SYSMAN", "1");

        if (File.Exists(bundledAdapter) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UR_ADAPTERS_FORCE_LOAD")))
            Environment.SetEnvironmentVariable("UR_ADAPTERS_FORCE_LOAD", bundledAdapter);

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ONEAPI_DEVICE_SELECTOR")))
            Environment.SetEnvironmentVariable("ONEAPI_DEVICE_SELECTOR", "level_zero:gpu");

    }
}