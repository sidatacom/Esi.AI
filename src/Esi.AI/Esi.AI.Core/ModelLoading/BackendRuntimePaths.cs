using System.Runtime.InteropServices;

namespace Esi.AI.Core.ModelLoading;

/// <summary>Resolves the isolated runtime directory assigned to each backend.</summary>
public static class BackendRuntimePaths
{
    private static readonly object NativeLibraryLock = new();
    private static readonly Dictionary<string, IntPtr> PreloadedNativeLibraries = new(StringComparer.Ordinal);

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

    /// <summary>Returns CUDA library directories from the managed vLLM CUDA environment.</summary>
    public static IReadOnlyList<string> GetCudaLibraryDirectories()
    {
        var environmentPath = PythonBackendProvisioner.GetDefaultEnvironmentPath(Esi.AI.Models.ConfigurationBackend.Vllm);
        var libraryRoot = Path.Combine(environmentPath, "lib");
        if (!Directory.Exists(libraryRoot))
            return [];

        return Directory.EnumerateDirectories(libraryRoot, "python*", SearchOption.TopDirectoryOnly)
            .Select(pythonDirectory => Path.Combine(pythonDirectory, "site-packages", "nvidia"))
            .Where(Directory.Exists)
            .SelectMany(nvidiaDirectory => Directory.EnumerateDirectories(nvidiaDirectory))
            .Select(packageDirectory => Path.Combine(packageDirectory, "lib"))
            .Where(Directory.Exists)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Loads the CUDA 12 dependencies required by the packaged LLama runtime.</summary>
    public static void PreloadCuda12RuntimeDependencies()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var directories = GetCudaLibraryDirectories();
        foreach (var libraryName in new[] { "libcudart.so.12", "libcublas.so.12" })
        {
            var libraryPath = directories
                .Select(directory => Path.Combine(directory, libraryName))
                .FirstOrDefault(File.Exists)
                ?? throw new DllNotFoundException(
                    $"Could not find '{libraryName}' in the managed CUDA library directories: {string.Join(Path.PathSeparator, directories)}");

            lock (NativeLibraryLock)
            {
                if (!PreloadedNativeLibraries.ContainsKey(libraryPath))
                    PreloadedNativeLibraries.Add(libraryPath, NativeLibrary.Load(libraryPath));
            }
        }
    }

    /// <summary>Adds the managed CUDA library directories to the current process search path.</summary>
    public static void PrependCudaLibraryPaths()
    {
        foreach (var directory in GetCudaLibraryDirectories())
            PrependLibraryPath(directory);
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

    /// <summary>Prepares Level Zero system management and the default Intel GPU selector for SYCL.</summary>
    public static void PrepareSyclRuntimeEnvironment(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        if (!OperatingSystem.IsLinux())
            return;

        var runtimeDirectory = GetLlamaDirectory("SYCL", applicationDirectory);
        var bundledLoader = Path.Combine(runtimeDirectory, "libze_loader.so.1");
        var bundledAdapter = Path.Combine(runtimeDirectory, "libur_adapter_level_zero_v2.so.0");
        if (File.Exists(bundledLoader) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ZES_ENABLE_SYSMAN")))
            Environment.SetEnvironmentVariable("ZES_ENABLE_SYSMAN", "1");

        if (File.Exists(bundledAdapter) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UR_ADAPTERS_FORCE_LOAD")))
            Environment.SetEnvironmentVariable("UR_ADAPTERS_FORCE_LOAD", bundledAdapter);

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ONEAPI_DEVICE_SELECTOR")))
            Environment.SetEnvironmentVariable("ONEAPI_DEVICE_SELECTOR", "level_zero:gpu");
    }
}