using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Esi.AI.Models;
using Microsoft.Extensions.Options;

namespace Esi.AI.Core.ModelLoading;

/// <summary>Downloads, verifies, and atomically activates native backend runtime packages.</summary>
public sealed class BackendRuntimeInstaller
{
    private static readonly JsonSerializerOptions CatalogJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly IReadOnlyDictionary<string, string[]> DefaultRequiredFiles = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["cuda12"] = ["libllama.so", "libggml.so", "libggml-base.so", "libggml-cuda.so"],
        ["sycl"] = ["libllama.so", "libggml.so", "libggml-base.so", "libggml-sycl.so"],
        ["vulkan"] = ["libllama.so", "libggml.so", "libggml-base.so", "libggml-vulkan.so"],
        ["cpu"] = ["libllama.so", "libggml.so", "libggml-base.so"]
    };

    private readonly HttpClient httpClient;
    private readonly BackendRuntimeOptions options;
    private readonly SemaphoreSlim catalogLock = new(1, 1);
    private readonly SemaphoreSlim installLock = new(1, 1);
    private IReadOnlyList<BackendRuntimePackage>? catalog;

    /// <summary>Creates a backend runtime installer using the configured gallery.</summary>
    public BackendRuntimeInstaller(HttpClient httpClient, IOptions<BackendRuntimeOptions> options, string? applicationDirectory = null)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        ApplicationDirectory = Path.GetFullPath(applicationDirectory ?? this.options.InstallationDirectory ?? AppContext.BaseDirectory);
    }

    /// <summary>Gets the application directory that contains the native runtime folders.</summary>
    public string ApplicationDirectory { get; }

    /// <summary>Returns whether a configured package can repair the selected route.</summary>
    public bool CanInstall(ConfigurationBackend backend, string route) =>
        options.Packages.Any(package => package.Backend == backend && NormalizeRoute(package.Route) == NormalizeRoute(route));

    /// <summary>Returns whether the configured or remote gallery can repair the selected route.</summary>
    public async Task<bool> CanInstallAsync(ConfigurationBackend backend, string route, CancellationToken cancellationToken = default) =>
        (await GetPackagesAsync(cancellationToken).ConfigureAwait(false))
            .Any(package => package.Backend == backend && NormalizeRoute(package.Route) == NormalizeRoute(route));

    /// <summary>Finds the gallery package that repairs the selected route.</summary>
    public async Task<BackendRuntimePackage?> FindPackageAsync(ConfigurationBackend backend, string route, CancellationToken cancellationToken = default) =>
        (await GetPackagesAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(package => package.Backend == backend && NormalizeRoute(package.Route) == NormalizeRoute(route));

    /// <summary>Finds a gallery package by its stable package identifier.</summary>
    public async Task<BackendRuntimePackage?> FindPackageByIdAsync(string packageId, CancellationToken cancellationToken = default) =>
        (await GetPackagesAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(package => package.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads the gallery packages and their installed state.</summary>
    public async Task<IReadOnlyList<BackendRuntimeStatus>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var packages = await GetPackagesAsync(cancellationToken).ConfigureAwait(false);
        return packages.Select(CreateStatus).ToArray();
    }

    /// <summary>Installs one package after validating its archive and native files.</summary>
    public async Task<BackendRuntimeStatus> InstallAsync(BackendRuntimeInstallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await installLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? stagingDirectory = null;
        BackendRuntimePackage? package = null;
        try
        {
            package = (await GetPackagesAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(candidate => candidate.Id.Equals(request.PackageId, StringComparison.OrdinalIgnoreCase));
            if (package is null)
                return FailedStatus(request.PackageId, "The selected backend runtime package is not available in the gallery.");

            ValidatePackage(package);
            var targetDirectory = GetTargetDirectory(package);
            var requiredFiles = GetRequiredFiles(package);
            if (requiredFiles.All(file => File.Exists(Path.Combine(targetDirectory, file))))
                return CreateStatus(package) with { Message = "The backend runtime is already installed." };
            if (Directory.Exists(targetDirectory) && Directory.EnumerateFileSystemEntries(targetDirectory).Any())
                return FailedStatus(package, "The native runtime directory contains files and cannot be replaced while Studio is running.");

            stagingDirectory = Path.Combine(Path.GetDirectoryName(targetDirectory)!, $".esi-runtime-{Guid.NewGuid():N}");
            var archivePath = Path.Combine(stagingDirectory, "package.zip");
            var extractedDirectory = Path.Combine(stagingDirectory, "extracted");
            var activationDirectory = Path.Combine(stagingDirectory, "activation");
            Directory.CreateDirectory(stagingDirectory);
            var sourceDirectory = await PrepareRuntimeSourceAsync(package, archivePath, extractedDirectory, cancellationToken).ConfigureAwait(false);
            CopyRequiredFiles(sourceDirectory, activationDirectory, requiredFiles);
            await File.WriteAllTextAsync(
                Path.Combine(activationDirectory, ".esi-runtime.json"),
                JsonSerializer.Serialize(new { package.Version, packageId = package.Id, package.Route }),
                cancellationToken).ConfigureAwait(false);

            Directory.CreateDirectory(Path.GetDirectoryName(targetDirectory)!);
            if (Directory.Exists(targetDirectory))
                Directory.Delete(targetDirectory, recursive: true);
            Directory.Move(activationDirectory, targetDirectory);
            return CreateStatus(package) with
            {
                State = BackendRuntimeState.Installed,
                IsInstalled = true,
                Message = package.RequiresRestart
                    ? "Backend runtime installed. Restart Studio before loading a model."
                    : "Backend runtime installed."
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return package is null
                ? FailedStatus(request.PackageId, exception.Message)
                : FailedStatus(package, exception.Message);
        }
        finally
        {
            if (stagingDirectory is not null && Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
            installLock.Release();
        }
    }

    private async Task<IReadOnlyList<BackendRuntimePackage>> GetPackagesAsync(CancellationToken cancellationToken)
    {
        if (catalog is not null)
            return catalog;

        await catalogLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (catalog is not null)
                return catalog;

            if (!string.IsNullOrWhiteSpace(options.CatalogUrl))
            {
                try
                {
                    ValidateRemoteUri(options.CatalogUrl);
                    var remoteCatalog = await httpClient.GetFromJsonAsync<BackendRuntimeCatalog>(options.CatalogUrl, CatalogJsonOptions, cancellationToken).ConfigureAwait(false);
                    if (remoteCatalog?.Packages is { Count: > 0 })
                        catalog = remoteCatalog.Packages;
                }
                catch when (!cancellationToken.IsCancellationRequested)
                {
                    catalog = options.Packages;
                }
            }

            catalog ??= options.Packages;
            return catalog;
        }
        finally
        {
            catalogLock.Release();
        }
    }

    private BackendRuntimeStatus CreateStatus(BackendRuntimePackage package)
    {
        try
        {
            ValidatePackage(package);
        }
        catch (Exception exception)
        {
            return new(package.Id, package.Backend, NormalizeRoute(package.Route), package.RuntimeIdentifier, package.Version,
                BackendRuntimeState.Failed, exception.Message, false, package.RequiresRestart, DateTimeOffset.UtcNow);
        }

        var isInstalled = GetRequiredFiles(package).All(file => File.Exists(Path.Combine(GetTargetDirectory(package), file)));
        return new(package.Id, package.Backend, NormalizeRoute(package.Route), package.RuntimeIdentifier, package.Version,
            isInstalled ? BackendRuntimeState.Installed : BackendRuntimeState.Failed,
            isInstalled ? "Backend runtime is installed." : "Backend runtime is not installed.", isInstalled, package.RequiresRestart, DateTimeOffset.UtcNow);
    }

    private BackendRuntimeStatus FailedStatus(BackendRuntimePackage package, string message) =>
        new(package.Id, package.Backend, NormalizeRoute(package.Route), package.RuntimeIdentifier, package.Version,
            BackendRuntimeState.Failed, message, false, package.RequiresRestart, DateTimeOffset.UtcNow);

    private static BackendRuntimeStatus FailedStatus(string packageId, string message) =>
        new(packageId, ConfigurationBackend.Llama, string.Empty, string.Empty, string.Empty,
            BackendRuntimeState.Failed, message, false, true, DateTimeOffset.UtcNow);

    private async Task DownloadArchiveAsync(string archiveUrl, string archivePath, CancellationToken cancellationToken)
    {
        ValidateRemoteUri(archiveUrl);
        using var response = await httpClient.GetAsync(archiveUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = File.Create(archivePath);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> PrepareRuntimeSourceAsync(
        BackendRuntimePackage package,
        string archivePath,
        string extractedDirectory,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(package.LocalPath))
        {
            var localPath = ResolveLocalPath(package.LocalPath);
            if (!Directory.Exists(localPath))
                throw new InvalidOperationException($"The local backend runtime directory does not exist: {localPath}");

            return localPath;
        }

        Directory.CreateDirectory(extractedDirectory);
        await DownloadArchiveAsync(package.ArchiveUrl, archivePath, cancellationToken).ConfigureAwait(false);
        await VerifySha256Async(archivePath, package.Sha256, cancellationToken).ConfigureAwait(false);
        ExtractArchiveSafely(archivePath, extractedDirectory);
        return extractedDirectory;
    }

    private static async Task VerifySha256Async(string archivePath, string expectedSha256, CancellationToken cancellationToken)
    {
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedSha256);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("The backend runtime package has an invalid SHA-256 value.");
        }

        if (expected.Length != 32)
            throw new InvalidOperationException("The backend runtime package has an invalid SHA-256 value.");

        await using var stream = File.OpenRead(archivePath);
        var actualSha256 = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(actualSha256, expected))
            throw new InvalidOperationException($"SHA-256 verification failed. Expected {expectedSha256}, got {Convert.ToHexString(actualSha256)}.");
    }

    private static void ExtractArchiveSafely(string archivePath, string destinationDirectory)
    {
        var fullDestination = Path.GetFullPath(destinationDirectory);
        var destinationPrefix = fullDestination.EndsWith(Path.DirectorySeparatorChar)
            ? fullDestination
            : fullDestination + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(fullDestination, entry.FullName));
            if (!target.StartsWith(destinationPrefix, StringComparison.Ordinal) && !target.Equals(fullDestination, StringComparison.Ordinal))
                throw new InvalidOperationException("The backend runtime archive contains an unsafe path.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = entry.Open();
            using var destination = File.Create(target);
            source.CopyTo(destination);
        }
    }

    private static void CopyRequiredFiles(string sourceDirectory, string activationDirectory, IReadOnlyList<string> requiredFiles)
    {
        Directory.CreateDirectory(activationDirectory);
        foreach (var requiredFile in requiredFiles)
        {
            var matches = Directory.EnumerateFiles(sourceDirectory, requiredFile, SearchOption.AllDirectories).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"The backend runtime source must contain exactly one '{requiredFile}' file.");
            File.Copy(matches[0], Path.Combine(activationDirectory, requiredFile));
        }
    }

    private string GetTargetDirectory(BackendRuntimePackage package) =>
        Path.Combine(ApplicationDirectory, "runtimes", package.RuntimeIdentifier, "native", NormalizeRoute(package.Route));

    private static IReadOnlyList<string> GetRequiredFiles(BackendRuntimePackage package) =>
        package.RequiredFiles.Count > 0
            ? package.RequiredFiles
            : DefaultRequiredFiles.TryGetValue(NormalizeRoute(package.Route), out var defaults) ? defaults : [];

    private void ValidatePackage(BackendRuntimePackage package)
    {
        if (string.IsNullOrWhiteSpace(package.Id) || string.IsNullOrWhiteSpace(package.Version))
            throw new InvalidOperationException("The backend runtime package must have an id and version.");
        if (package.Backend != ConfigurationBackend.Llama)
            throw new InvalidOperationException("Only LLama native runtime packages are supported by this installer.");
        if (NormalizeRoute(package.Route) is not ("cuda12" or "sycl" or "vulkan" or "cpu"))
            throw new InvalidOperationException("The backend runtime route is not supported.");
        if (string.IsNullOrWhiteSpace(package.RuntimeIdentifier))
            throw new InvalidOperationException("The backend runtime package must specify a runtime identifier.");
        if (package.RuntimeIdentifier.Contains(Path.DirectorySeparatorChar) || package.RuntimeIdentifier.Contains(Path.AltDirectorySeparatorChar))
            throw new InvalidOperationException("The runtime identifier contains an invalid path separator.");
        var requiredFiles = GetRequiredFiles(package);
        if (requiredFiles.Count == 0)
            throw new InvalidOperationException("The backend runtime package does not declare native files.");
        if (requiredFiles.Any(file => string.IsNullOrWhiteSpace(file) || Path.GetFileName(file) != file))
            throw new InvalidOperationException("The backend runtime package contains an invalid native file name.");
        if (!string.IsNullOrWhiteSpace(package.LocalPath))
        {
            if (!options.AllowLocalPackages)
                throw new InvalidOperationException("Local backend runtime packages are disabled.");
        }
        else
        {
            ValidateRemoteUri(package.ArchiveUrl);
        }
    }

    private string ResolveLocalPath(string path) =>
        Path.GetFullPath(Environment.ExpandEnvironmentVariables(path), ApplicationDirectory);

    private static void ValidateRemoteUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Backend runtime downloads must use HTTPS URLs.");
    }

    private static string NormalizeRoute(string route) => route.Trim().ToLowerInvariant() switch
    {
        "cuda" => "cuda12",
        "cuda12" => "cuda12",
        "xpu" => "sycl",
        "sycl16" => "sycl",
        "sycl" => "sycl",
        "gpu-vulkan" => "vulkan",
        _ => route.Trim().ToLowerInvariant()
    };
}