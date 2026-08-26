using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using YamlDotNet.RepresentationModel;

namespace Esi.AI.Studio.Services;

public sealed class ModelLibraryService
{
    private readonly HttpClient httpClient;
    private readonly ModelLibraryOptions options;
    private readonly ConcurrentDictionary<Guid, ModelDownloadStatus> downloads = new();

    public ModelLibraryService(HttpClient httpClient, IOptions<ModelLibraryOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
    }

    public IReadOnlyList<string> GetModelDirectories() => GetDirectories();

    public async Task<IReadOnlyList<LocalModelInfo>> ScanLocalModelsAsync(CancellationToken cancellationToken = default)
    {
        var directories = GetDirectories();
        var models = new Dictionary<string, LocalModelInfo>(StringComparer.OrdinalIgnoreCase);
        var referencedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configurationPath in EnumerateConfigurationFiles(directories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var model in ReadYamlModels(configurationPath, directories, referencedFiles))
                models[model.Path] = model;
        }

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var path in Directory.EnumerateFiles(directory, "*.gguf", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = new FileInfo(path);
                if (!referencedFiles.Contains(file.FullName))
                    models.TryAdd(file.FullName, new LocalModelInfo(file.Name, file.FullName, file.Length, file.LastWriteTimeUtc));
            }
        }

        return models
            .Values.OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateConfigurationFiles(IReadOnlyList<string> directories)
    {
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var path in Directory.EnumerateFiles(directory, "*.yaml", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(directory, "*.yml", SearchOption.TopDirectoryOnly)))
            {
                if (!Path.GetFileName(path).StartsWith("._", StringComparison.Ordinal))
                    yield return path;
            }
        }
    }

    private static IEnumerable<LocalModelInfo> ReadYamlModels(
        string configurationPath,
        IReadOnlyList<string> directories,
        ISet<string> referencedFiles)
    {
        YamlMappingNode root;
        using (var reader = File.OpenText(configurationPath))
        {
            var yaml = new YamlStream();
            yaml.Load(reader);
            if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode mapping)
                yield break;
            root = mapping;
        }

        var name = GetScalar(root, "name");
        var parameters = GetMapping(root, "parameters");
        var modelValue = parameters is null ? null : GetScalar(parameters, "model");
        if (string.IsNullOrWhiteSpace(modelValue))
            yield break;

        var modelPath = ResolvePath(modelValue, configurationPath, directories);
        referencedFiles.Add(modelPath);
        AddReferencedPath(root, "draft_model", configurationPath, directories, referencedFiles);
        AddReferencedPath(root, "mmproj", configurationPath, directories, referencedFiles);

        if (!File.Exists(modelPath))
            yield break;

        var file = new FileInfo(modelPath);
        yield return new LocalModelInfo(
            string.IsNullOrWhiteSpace(name) ? file.Name : name,
            file.FullName,
            file.Length,
            file.LastWriteTimeUtc);
    }

    private static void AddReferencedPath(
        YamlMappingNode root,
        string key,
        string configurationPath,
        IReadOnlyList<string> directories,
        ISet<string> referencedFiles)
    {
        var value = GetScalar(root, key);
        if (!string.IsNullOrWhiteSpace(value))
            referencedFiles.Add(ResolvePath(value, configurationPath, directories));
    }

    private static string ResolvePath(string value, string configurationPath, IReadOnlyList<string> directories)
    {
        var configurationDirectory = Path.GetDirectoryName(configurationPath)!;
        var candidates = new[]
        {
            value,
            Path.Combine(configurationDirectory, value),
            Path.Combine(Directory.GetParent(configurationDirectory)?.FullName ?? configurationDirectory, value)
        }.Concat(directories.Select(directory => Path.Combine(directory, value)));

        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists)
            ?? Path.GetFullPath(Path.Combine(configurationDirectory, value));
    }

    private static string? GetScalar(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar
            ? scalar.Value
            : null;

    private static YamlMappingNode? GetMapping(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlMappingNode child
            ? child
            : null;

    public async Task<IReadOnlyList<HuggingFaceModelInfo>> SearchHuggingFaceAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"api/models?search={Uri.EscapeDataString(query ?? string.Empty)}&filter=gguf&limit={Math.Clamp(options.SearchLimit, 1, 100)}&sort=downloads&direction=-1";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var models = await response.Content.ReadFromJsonAsync<List<HuggingFaceModelInfo>>(cancellationToken: cancellationToken) ?? [];
        return models;
    }

    public async Task<Guid> StartDownloadAsync(string modelId, string? fileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId) || modelId.Split('/').Length != 2)
            throw new ArgumentException("A Hugging Face model id in the form owner/repository is required.", nameof(modelId));

        var selectedFile = await ResolveGgufFileAsync(modelId, fileName, cancellationToken);
        var directory = GetDirectories().FirstOrDefault() ?? throw new InvalidOperationException("No model directory is configured.");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, Path.GetFileName(selectedFile));
        var downloadId = Guid.NewGuid();
        downloads[downloadId] = new ModelDownloadStatus(downloadId, modelId, selectedFile, destination, 0, null, false, null);
        _ = DownloadAsync(downloadId, modelId, selectedFile, destination);
        return downloadId;
    }

    public ModelDownloadStatus? GetDownload(Guid downloadId) =>
        downloads.TryGetValue(downloadId, out var status) ? status : null;

    private async Task<string> ResolveGgufFileAsync(string modelId, string? requestedFile, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedFile) && requestedFile.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileName(requestedFile);

        using var response = await httpClient.GetAsync($"api/models/{modelId}", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var files = document.RootElement.TryGetProperty("siblings", out var siblings)
            ? siblings.EnumerateArray()
                .Select(item => item.TryGetProperty("rfilename", out var name) ? name.GetString() : null)
                .Where(name => name is not null && name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                .Select(name => name!)
                .ToArray()
            : [];
        return files.FirstOrDefault() ?? throw new InvalidOperationException("The Hugging Face repository does not contain a GGUF file.");
    }

    private async Task DownloadAsync(Guid downloadId, string modelId, string fileName, string destination)
    {
        try
        {
            using var response = await httpClient.GetAsync($"{Uri.EscapeDataString(modelId).Replace("%2F", "/")}/resolve/main/{Uri.EscapeDataString(fileName)}", HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
            var buffer = new byte[1024 * 1024];
            long downloaded = 0;
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                downloads[downloadId] = new ModelDownloadStatus(downloadId, modelId, fileName, destination, downloaded, total, false, null);
            }
            downloads[downloadId] = new ModelDownloadStatus(downloadId, modelId, fileName, destination, downloaded, total, true, null);
        }
        catch (Exception exception)
        {
            downloads[downloadId] = new ModelDownloadStatus(downloadId, modelId, fileName, destination, 0, null, false, exception.Message);
        }
    }

    private IReadOnlyList<string> GetDirectories()
    {
        var directories = options.Directories
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(ExpandDirectory)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (directories.Count == 0)
            directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "esi-ai", "models"));
        return directories;
    }

    private static string ExpandDirectory(string directory)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        directory = directory.Replace("%USERPROFILE%", userProfile, StringComparison.OrdinalIgnoreCase);
        if (directory.StartsWith("~/", StringComparison.Ordinal))
            directory = Path.Combine(userProfile, directory[2..]);
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory));
    }
}

public sealed class ModelLibraryOptions
{
    public List<string> Directories { get; set; } = [];
    public int SearchLimit { get; set; } = 20;
}

public sealed record LocalModelInfo(string Name, string Path, long SizeInBytes, DateTime LastWriteTimeUtc);

public sealed record HuggingFaceModelInfo(string Id, string? Author, long Downloads, long Likes, DateTime? LastModified);

public sealed record ModelDownloadStatus(Guid Id, string ModelId, string FileName, string DestinationPath, long BytesDownloaded, long? TotalBytes, bool Completed, string? Error);
