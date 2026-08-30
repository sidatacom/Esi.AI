using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Esi.AI.Models;
using Esi.AI.Studio.Data;
using Esi.AI.Studio.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using YamlDotNet.RepresentationModel;

namespace Esi.AI.Studio.Services;

public sealed class ModelLibraryService : IAsyncDisposable
{
    private readonly HttpClient httpClient;
    private readonly IHubContext<DataHub> hubContext;
    private readonly IDbContextFactory<ApplicationDbContext> dbContextFactory;
    private readonly ModelLibraryOptions options;
    private readonly ConcurrentDictionary<Guid, ModelDownloadStatus> downloads = new();
    private readonly ConcurrentDictionary<Guid, DownloadOperation> downloadOperations = new();
    private readonly Channel<DownloadOperation> downloadQueue = Channel.CreateUnbounded<DownloadOperation>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource queueCancellation = new();
    private readonly SemaphoreSlim downloadSlots;
    private readonly SemaphoreSlim fileDownloadSlots;
    private readonly Task queueWorker;

    public ModelLibraryService(
        HttpClient httpClient,
        IHubContext<DataHub> hubContext,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IOptions<ModelLibraryOptions> options)
    {
        this.httpClient = httpClient;
        this.hubContext = hubContext;
        this.dbContextFactory = dbContextFactory;
        this.options = options.Value;
        downloadSlots = new(Math.Max(1, this.options.MaxParallelDownloads));
        fileDownloadSlots = new(Math.Max(1, this.options.MaxParallelFileDownloads));
        queueWorker = ProcessDownloadQueueAsync(queueCancellation.Token);
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

            foreach (var model in ScanOpenVinoModels([directory], cancellationToken))
                models.TryAdd(model.Path, model);

            foreach (var model in ScanTransformersModels([directory], cancellationToken))
                models.TryAdd(model.Path, model);
        }

        return models
            .Values.OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<LocalModelInfo>> ScanLocalModelsAsync(
        ConfigurationBackend backend,
        CancellationToken cancellationToken = default)
    {
        var models = await ScanLocalModelsAsync(cancellationToken);
        if (backend == ConfigurationBackend.OpenVino)
            return models.Where(model => model.Format == ReferenceModelFormat.OpenVinoIr).ToArray();

        return backend is ConfigurationBackend.Llama or ConfigurationBackend.DotLlm
            ? models.Where(model => model.Format == ReferenceModelFormat.Gguf).ToArray()
            : [];
    }

    private static IReadOnlyList<LocalModelInfo> ScanOpenVinoModels(
        IReadOnlyList<string> directories,
        CancellationToken cancellationToken)
    {
        var models = new Dictionary<string, LocalModelInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var marker in Directory.EnumerateFiles(directory, "openvino_language_model.xml", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var modelDirectory = Path.GetDirectoryName(marker)!;
                var files = Directory.EnumerateFiles(modelDirectory, "*", SearchOption.AllDirectories).ToArray();
                var lastWriteTime = files.Length == 0
                    ? File.GetLastWriteTimeUtc(modelDirectory)
                    : files.Max(File.GetLastWriteTimeUtc);
                var size = files.Sum(path => new FileInfo(path).Length);
                models[modelDirectory] = new LocalModelInfo(Path.GetFileName(modelDirectory), modelDirectory, size, lastWriteTime, ReferenceModelFormat.OpenVinoIr);
            }
        }

        return models.Values.OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<LocalModelInfo> ScanTransformersModels(
        IReadOnlyList<string> directories,
        CancellationToken cancellationToken)
    {
        var models = new Dictionary<string, LocalModelInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var configurationPath in Directory.EnumerateFiles(directory, "config.json", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var modelDirectory = Path.GetDirectoryName(configurationPath)!;
                if (!Directory.EnumerateFiles(modelDirectory, "*.safetensors", SearchOption.AllDirectories).Any())
                    continue;

                var files = Directory.EnumerateFiles(modelDirectory, "*", SearchOption.AllDirectories).ToArray();
                var lastWriteTime = files.Length == 0
                    ? File.GetLastWriteTimeUtc(modelDirectory)
                    : files.Max(File.GetLastWriteTimeUtc);
                var size = files.Sum(path => new FileInfo(path).Length);
                models[modelDirectory] = new LocalModelInfo(Path.GetFileName(modelDirectory), modelDirectory, size, lastWriteTime, ReferenceModelFormat.Transformers);
            }
        }

        return models.Values.OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase).ToArray();
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

        return candidates.FirstOrDefault(File.Exists)
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

    public async Task<IReadOnlyList<HuggingFaceModelInfo>> SearchHuggingFaceAsync(HuggingFaceSearchRequest request, CancellationToken cancellationToken = default)
    {
        var libraries = request.Libraries?
            .Where(library => !string.IsNullOrWhiteSpace(library) && !library.Equals("all", StringComparison.OrdinalIgnoreCase))
            .Select(library => library.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (libraries.Length > 1)
        {
            var results = await Task.WhenAll(libraries.Select(library =>
                SearchHuggingFaceQueryAsync(request with { Libraries = [library] }, cancellationToken)));
            var merged = new List<HuggingFaceModelInfo>();
            var seenModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resultLimit = Math.Clamp(options.SearchLimit, 1, 100);
            for (var index = 0; index < results.Max(models => models.Count) && merged.Count < resultLimit; index++)
            {
                foreach (var models in results)
                {
                    if (index >= models.Count || !seenModelIds.Add(models[index].Id))
                        continue;
                    merged.Add(models[index]);
                    if (merged.Count == resultLimit)
                        break;
                }
            }

            return merged;
        }

        return await SearchHuggingFaceQueryAsync(
            libraries.Length == 1 ? request with { Libraries = libraries } : request with { Libraries = null },
            cancellationToken);
    }

    private async Task<IReadOnlyList<HuggingFaceModelInfo>> SearchHuggingFaceQueryAsync(
        HuggingFaceSearchRequest request,
        CancellationToken cancellationToken)
    {
        var queryParts = new List<string> { $"search={Uri.EscapeDataString(request.Query ?? string.Empty)}" };
        AddQueryParts(queryParts, "filter", request.Libraries);
        AddQueryParts(queryParts, "pipeline_tag", request.Tasks);
        AddQueryParts(queryParts, "num_parameters", request.ParameterRanges);
        AddQueryParts(queryParts, "language", request.Languages);
        AddQueryParts(queryParts, "license", request.Licenses);
        AddQueryParts(queryParts, "hardware", request.Hardware);
        if (request.BaseOnly)
            AddQueryPart(queryParts, "other", "base");
        AddQueryParts(queryParts, "other", request.Other);
        AddQueryParts(queryParts, "inference_provider", request.InferenceAvailable ? ["all"] : request.InferenceProviders);
        if (!string.IsNullOrWhiteSpace(request.Sort) && !request.Sort.Equals("trending", StringComparison.OrdinalIgnoreCase))
            AddQueryPart(queryParts, "sort", request.Sort);
        queryParts.Add("limit=" + Math.Clamp(options.SearchLimit, 1, 100));
        queryParts.Add("direction=-1");
        var url = "api/models?" + string.Join('&', queryParts);
        using var response = await httpClient.GetAsync(url, cancellationToken);
        EnsureHuggingFaceSuccessStatusCode(response);
        var models = await response.Content.ReadFromJsonAsync<List<HuggingFaceModelInfo>>(cancellationToken: cancellationToken) ?? [];
        return models;
    }

    public async Task<HuggingFaceRepositoryMetadata> GetHuggingFaceModelMetadataAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        ValidateModelId(modelId);
        var repository = await ResolveRepositoryAsync(modelId, cancellationToken);
        return new(modelId, repository.Revision, repository.LibraryName, repository.PipelineTag, repository.Tags ?? []);
    }

    private static void AddQueryPart(List<string> queryParts, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !value.Equals("all", StringComparison.OrdinalIgnoreCase))
            queryParts.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
    }

    private static void AddQueryParts(List<string> queryParts, string name, IEnumerable<string>? values)
    {
        if (values is null)
            return;

        foreach (var value in values)
            AddQueryPart(queryParts, name, value);
    }

    public async Task<Guid> StartDownloadAsync(string modelId, string? fileName, string library = "gguf", CancellationToken cancellationToken = default)
    {
        ValidateModelId(modelId);

        var normalizedLibrary = library.ToLowerInvariant();
        var repository = await ResolveRepositoryAsync(modelId, cancellationToken);
        var selectedFiles = normalizedLibrary == "gguf"
            ? SelectGgufFiles(repository.Files, fileName)
            : SelectRepositoryFiles(repository.Files);
        if (selectedFiles.Count == 0)
            throw new InvalidOperationException("The Hugging Face repository does not contain downloadable files.");
        var directory = GetDirectories().FirstOrDefault() ?? throw new InvalidOperationException("No model directory is configured.");
        Directory.CreateDirectory(directory);
        var destination = normalizedLibrary == "gguf"
            ? directory
            : Path.Combine(directory, Path.GetFileName(modelId));
        Directory.CreateDirectory(destination);
        var downloadId = Guid.NewGuid();
        var operation = new DownloadOperation(downloadId, modelId, normalizedLibrary, selectedFiles.Select(file => file.Name).ToArray(), destination, repository.Revision);
        operation.InitializeFileStatuses(
            selectedFiles.Select(file => GetDownloadFilePath(destination, file.Name)).ToArray(),
            selectedFiles.Select(file => new DownloadFileStatus(file.Name, 0, file.SizeInBytes, false)).ToArray());
        var initialStatus = CreateDownloadStatus(operation, queued: true);
        downloads[downloadId] = initialStatus;
        downloadOperations[downloadId] = operation;
        await PersistDownloadAsync(operation, initialStatus, cancellationToken);
        await PublishDownloadUpdateAsync(initialStatus, eventName: "ModelDownload_Create");
        await downloadQueue.Writer.WriteAsync(operation, cancellationToken);
        return downloadId;
    }

    public async Task<IReadOnlyList<ModelDownloadOption>> GetDownloadOptionsAsync(string modelId, string library = "gguf", CancellationToken cancellationToken = default)
    {
        ValidateModelId(modelId);

        if (library.Equals("openvino", StringComparison.OrdinalIgnoreCase))
            return [new ModelDownloadOption("OpenVINO repository", 0)];

        var repository = await ResolveRepositoryAsync(modelId, cancellationToken);
        if (!library.Equals("gguf", StringComparison.OrdinalIgnoreCase))
        {
            var repositoryFiles = SelectRepositoryFiles(repository.Files);
            var size = repositoryFiles.All(file => file.SizeInBytes is not null)
                ? (long?)repositoryFiles.Sum(file => file.SizeInBytes!.Value)
                : null;
            return [new ModelDownloadOption(string.Empty, repositoryFiles.Count, size)];
        }

        var files = SelectGgufFiles(repository.Files, null, requireSelection: false);
        return files
            .GroupBy(file => GetGgufSetKey(file.Name), StringComparer.OrdinalIgnoreCase)
            .Select(group => new ModelDownloadOption(
                group.OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase).First().Name,
                group.Count(),
                group.All(file => file.SizeInBytes is not null) ? group.Sum(file => file.SizeInBytes!.Value) : null))
            .OrderBy(option => option.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ValidateModelId(string modelId)
    {
        var segments = modelId.Split('/');
        if (string.IsNullOrWhiteSpace(modelId) || segments.Length != 2 || segments.Any(string.IsNullOrWhiteSpace) || segments.Any(segment => segment is "." or ".." || segment.Contains('\\')))
            throw new ArgumentException("A Hugging Face model id in the form owner/repository is required.", nameof(modelId));
    }

    public async Task PauseDownloadAsync(Guid downloadId, CancellationToken cancellationToken = default)
    {
        if (!downloadOperations.TryGetValue(downloadId, out var operation))
            return;

        operation.RequestPause();
        if (operation.Task is not null)
            await operation.Task.WaitAsync(cancellationToken);
    }

    public async Task ResumeDownloadAsync(Guid downloadId, CancellationToken cancellationToken = default)
    {
        if (!downloadOperations.TryGetValue(downloadId, out var operation) || !downloads.TryGetValue(downloadId, out var status) || !status.Paused)
            return;

        operation.Resume();
        operation.PrepareForQueue();
        var resumed = status with { Paused = false, Error = null, Queued = true };
        downloads[downloadId] = resumed;
        await PersistDownloadAsync(operation, resumed, cancellationToken);
        await PublishDownloadUpdateAsync(resumed);
        await downloadQueue.Writer.WriteAsync(operation, cancellationToken);
    }

    public async Task CancelDownloadAsync(Guid downloadId, CancellationToken cancellationToken = default)
    {
        if (!downloadOperations.TryGetValue(downloadId, out var operation) ||
            !downloads.TryGetValue(downloadId, out var status) ||
            status.Completed)
            return;

        operation.RequestCancel();
        await operation.Task.WaitAsync(cancellationToken);

        if (downloads.TryGetValue(downloadId, out var completed) && completed.Completed)
            return;

        DeleteDownloadFiles(operation);
        downloads.TryRemove(downloadId, out _);
        downloadOperations.TryRemove(downloadId, out _);
        await DeletePersistedDownloadAsync(downloadId, cancellationToken);
        await PublishDownloadUpdateAsync(status with { Error = null, Paused = false, Queued = false }, cancelled: true, eventName: "ModelDownload_Delete");
    }

    public async Task RestoreDownloadsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var persistedDownloads = await db.ModelDownloads
            .AsNoTracking()
            .Where(download => !download.Completed)
            .ToArrayAsync(cancellationToken);

        foreach (var persistedDownload in persistedDownloads)
        {
            var fileNames = JsonSerializer.Deserialize<string[]>(persistedDownload.FileNamesJson) ?? [];
            var fileStatuses = JsonSerializer.Deserialize<DownloadFileStatus[]>(persistedDownload.FileStatusesJson) ?? [];
            if (fileNames.Length == 0)
                continue;

            var operation = new DownloadOperation(
                persistedDownload.Id,
                persistedDownload.ModelId,
                persistedDownload.Library,
                fileNames,
                persistedDownload.DestinationPath,
                persistedDownload.Revision);
            var filePaths = fileNames.Select(fileName => GetDownloadFilePath(persistedDownload.DestinationPath, fileName)).ToArray();
            operation.InitializeFileStatuses(filePaths, fileStatuses);

            var allFilesCompleted = operation.FileStatuses.All(file => file.Completed);
            var needsManualResume = !allFilesCompleted && string.IsNullOrWhiteSpace(persistedDownload.Error);
            var status = CreateDownloadStatus(operation, completed: allFilesCompleted) with
            {
                Error = persistedDownload.Error,
                Paused = needsManualResume || (persistedDownload.Paused && !allFilesCompleted)
            };
            downloads[operation.Id] = status;
            downloadOperations[operation.Id] = operation;
            operation.Completion.TrySetResult();

            if (allFilesCompleted || needsManualResume)
            {
                await PersistDownloadAsync(operation, status, cancellationToken);
            }
        }
    }

    public ModelDownloadStatus? GetDownload(Guid downloadId) =>
        downloads.TryGetValue(downloadId, out var status) ? status : null;

    /// <summary>Returns the current in-memory state of all model downloads.</summary>
    public IReadOnlyList<ModelDownloadStatus> GetDownloads() =>
        downloads.Values.OrderByDescending(status => status.Id).ToArray();

    public async ValueTask DisposeAsync()
    {
        downloadQueue.Writer.TryComplete();
        foreach (var operation in downloadOperations.Values)
            operation.RequestPause();
        queueCancellation.Cancel();
        try
        {
            await queueWorker;
        }
        catch (OperationCanceledException)
        {
        }
        queueCancellation.Dispose();
        downloadSlots.Dispose();
        fileDownloadSlots.Dispose();
    }

    private async Task ProcessDownloadQueueAsync(CancellationToken cancellationToken)
    {
        var runningDownloads = new List<Task>();
        await foreach (var operation in downloadQueue.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await downloadSlots.WaitAsync(operation.Token);
            }
            catch (OperationCanceledException) when (operation.CancelRequested)
            {
                operation.Completion.TrySetResult();
                continue;
            }

            runningDownloads.Add(ProcessQueuedDownloadAsync(operation));
        }
        await Task.WhenAll(runningDownloads);
    }

    private async Task ProcessQueuedDownloadAsync(DownloadOperation operation)
    {
        try
        {
            await DownloadAsync(operation);
        }
        finally
        {
            downloadSlots.Release();
            operation.Completion.TrySetResult();
        }
    }

    private async Task<RepositorySnapshot> ResolveRepositoryAsync(string modelId, CancellationToken cancellationToken)
    {
        var modelSegments = modelId.Split('/');
        using var response = await httpClient.GetAsync($"api/models/{Uri.EscapeDataString(modelSegments[0])}/{Uri.EscapeDataString(modelSegments[1])}", cancellationToken);
        EnsureHuggingFaceSuccessStatusCode(response);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var revision = document.RootElement.TryGetProperty("sha", out var sha) && sha.ValueKind == JsonValueKind.String && sha.GetString() is { Length: > 0 } value
            ? value
            : "main";
        var libraryName = document.RootElement.TryGetProperty("library_name", out var library) && library.ValueKind == JsonValueKind.String
            ? library.GetString()
            : null;
        var pipelineTag = document.RootElement.TryGetProperty("pipeline_tag", out var pipeline) && pipeline.ValueKind == JsonValueKind.String
            ? pipeline.GetString()
            : null;
        var tags = document.RootElement.TryGetProperty("tags", out var tagValues) && tagValues.ValueKind == JsonValueKind.Array
            ? tagValues.EnumerateArray()
                .Where(tag => tag.ValueKind == JsonValueKind.String)
                .Select(tag => tag.GetString())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag!)
                .ToArray()
            : [];
        var files = document.RootElement.TryGetProperty("siblings", out var siblings)
            ? siblings.EnumerateArray()
                .Select(item => item.TryGetProperty("rfilename", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToArray()
            : [];
        var fileSizes = await ResolveFileSizesAsync(modelSegments, revision, cancellationToken);
        var repositoryFiles = files.Select(file => new RepositoryFile(file, fileSizes.GetValueOrDefault(file))).ToArray();
        return repositoryFiles.Length > 0
            ? new RepositorySnapshot(repositoryFiles, revision, libraryName, pipelineTag, tags)
            : throw new InvalidOperationException("The Hugging Face repository does not contain downloadable files.");
    }

    private async Task<IReadOnlyDictionary<string, long>> ResolveFileSizesAsync(string[] modelSegments, string revision, CancellationToken cancellationToken)
    {
        var treeUrl = $"api/models/{Uri.EscapeDataString(modelSegments[0])}/{Uri.EscapeDataString(modelSegments[1])}/tree/{Uri.EscapeDataString(revision)}?recursive=true";
        using var response = await httpClient.GetAsync(treeUrl, cancellationToken);
        EnsureHuggingFaceSuccessStatusCode(response);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind is not JsonValueKind.Array)
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        return document.RootElement.EnumerateArray()
            .Where(item => item.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String
                && item.TryGetProperty("size", out var size) && size.TryGetInt64(out _))
            .ToDictionary(item => item.GetProperty("path").GetString()!, item => item.GetProperty("size").GetInt64(), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<RepositoryFile> SelectGgufFiles(IReadOnlyList<RepositoryFile> files, string? requestedFile, bool requireSelection = true)
    {
        var ggufFiles = files.Where(file => file.Name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (ggufFiles.Length == 0)
            throw new InvalidOperationException("The Hugging Face repository does not contain a GGUF file.");
        if (string.IsNullOrWhiteSpace(requestedFile))
        {
            if (requireSelection && ggufFiles.Length > 1)
                throw new InvalidOperationException("Select a GGUF quantization before starting the download.");
            return ggufFiles;
        }

        var selectedKey = GetGgufSetKey(Path.GetFileName(requestedFile));
        var selectedFiles = ggufFiles.Where(file => GetGgufSetKey(file.Name).Equals(selectedKey, StringComparison.OrdinalIgnoreCase)).ToArray();
        return selectedFiles.Length > 0
            ? selectedFiles
            : throw new InvalidOperationException("The selected GGUF file is not present in the Hugging Face repository.");
    }

    private static IReadOnlyList<RepositoryFile> SelectRepositoryFiles(IReadOnlyList<RepositoryFile> files)
    {
        var repositoryFiles = files.Where(file => !Path.GetFileName(file.Name).StartsWith(".", StringComparison.Ordinal)).ToArray();
        return repositoryFiles.Length > 0
            ? repositoryFiles
            : throw new InvalidOperationException("The Hugging Face repository does not contain downloadable files.");
    }

    private static string GetGgufSetKey(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var shardIndex = name.IndexOf("-000", StringComparison.OrdinalIgnoreCase);
        return shardIndex > 0 && name.Contains("-of-", StringComparison.OrdinalIgnoreCase)
            ? name[..shardIndex]
            : name;
    }

    private async Task DownloadAsync(DownloadOperation operation)
    {
        var downloadId = operation.Id;
        var modelId = operation.ModelId;
        var fileNames = operation.FileNames;
        var destination = operation.Destination;
        try
        {
            if (operation.CancelRequested)
                return;

            var filePaths = fileNames.Select(fileName => GetDownloadFilePath(destination, fileName)).ToArray();
            operation.InitializeFileStatuses(filePaths, operation.FileStatuses);
            await PublishDownloadStatusAsync(operation, false);
            var modelSegments = modelId.Split('/');
            await Task.WhenAll(fileNames.Select((fileName, index) =>
                DownloadFileAsync(operation, fileName, filePaths[index], modelSegments)));
            var completed = CreateDownloadStatus(operation, completed: true);
            downloads[downloadId] = completed;
            var localModels = await ScanLocalModelsAsync();
            await PersistDownloadAsync(operation, completed);
            await PublishDownloadUpdateAsync(completed, localModels);
        }
        catch (OperationCanceledException) when (operation.CancelRequested)
        {
        }
        catch (OperationCanceledException) when (operation.PauseRequested)
        {
            if (downloads.TryGetValue(downloadId, out var currentStatus))
            {
                var paused = currentStatus with { Paused = true, Error = null };
                downloads[downloadId] = paused;
                await PersistDownloadAsync(operation, paused);
                await PublishDownloadUpdateAsync(paused);
            }
        }
        catch (Exception exception)
        {
            var currentStatus = downloads.TryGetValue(downloadId, out var status)
                ? status
                : CreateDownloadStatus(operation);
            var failed = currentStatus with { Error = exception.Message, Paused = false };
            downloads[downloadId] = failed;
            await PersistDownloadAsync(operation, failed);
            await PublishDownloadUpdateAsync(failed);
        }
    }

    private async Task DownloadFileAsync(DownloadOperation operation, string fileName, string filePath, string[] modelSegments)
    {
        await fileDownloadSlots.WaitAsync(operation.Token);
        try
        {
            var fileOffset = File.Exists(filePath) ? new FileInfo(filePath).Length : 0;
            var downloadUrl = $"{Uri.EscapeDataString(modelSegments[0])}/{Uri.EscapeDataString(modelSegments[1])}/resolve/{Uri.EscapeDataString(operation.Revision)}/{Uri.EscapeDataString(fileName)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            if (fileOffset > 0)
                request.Headers.Range = new RangeHeaderValue(fileOffset, null);
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, operation.Token);
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && fileOffset > 0)
            {
                var remoteLength = response.Content.Headers.ContentRange?.Length;
                response.Dispose();
                if (remoteLength == fileOffset)
                {
                    await PublishFileStatusAsync(operation, new DownloadFileStatus(fileName, fileOffset, remoteLength, true));
                    return;
                }

                fileOffset = 0;
                using var restartRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                response = await httpClient.SendAsync(restartRequest, HttpCompletionOption.ResponseHeadersRead, operation.Token);
            }

            using (response)
            {
                EnsureHuggingFaceSuccessStatusCode(response);
                var append = fileOffset > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                if (!append)
                    fileOffset = 0;
                var totalBytes = response.Content.Headers.ContentLength is { } contentLength
                    ? (long?)(fileOffset + contentLength)
                    : null;
                await PublishFileStatusAsync(operation, new DownloadFileStatus(fileName, fileOffset, totalBytes, false));
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                await using var source = await response.Content.ReadAsStreamAsync(operation.Token);
                await using var target = new FileStream(filePath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
                var downloaded = fileOffset;
                var buffer = new byte[1024 * 1024];
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(), operation.Token)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), operation.Token);
                    downloaded += read;
                    await PublishFileStatusAsync(operation, new DownloadFileStatus(fileName, downloaded, totalBytes, false));
                }
                await target.FlushAsync(operation.Token);
                await PublishFileStatusAsync(operation, new DownloadFileStatus(fileName, downloaded, totalBytes, true));
            }
        }
        finally
        {
            fileDownloadSlots.Release();
        }
    }

    private async Task PublishDownloadStatusAsync(DownloadOperation operation, bool queued)
    {
        var status = CreateDownloadStatus(operation, queued: queued);
        downloads[operation.Id] = status;
        operation.MarkStatusPublished(DateTimeOffset.UtcNow);
        await PersistDownloadAsync(operation, status);
        await PublishDownloadUpdateAsync(status);
    }

    private async Task PublishFileStatusAsync(DownloadOperation operation, DownloadFileStatus fileStatus)
    {
        ModelDownloadStatus status;
        lock (operation.SyncRoot)
        {
            operation.SetFileStatus(fileStatus);
            status = CreateDownloadStatus(operation);
            downloads[operation.Id] = status;
            if (!fileStatus.Completed && !operation.ShouldPublishStatus(DateTimeOffset.UtcNow))
                return;
        }
        await PersistDownloadAsync(operation, status);
        await PublishDownloadUpdateAsync(status);
    }

    private async Task PersistDownloadAsync(
        DownloadOperation operation,
        ModelDownloadStatus status,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ModelDownloads.SingleOrDefaultAsync(download => download.Id == operation.Id, cancellationToken);
        if (entity is null)
        {
            entity = new ModelDownloadEntity
            {
                Id = operation.Id,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.ModelDownloads.Add(entity);
        }

        entity.ModelId = operation.ModelId;
        entity.Library = operation.Library;
        entity.DestinationPath = operation.Destination;
        entity.Revision = operation.Revision;
        entity.FileNamesJson = JsonSerializer.Serialize(operation.FileNames);
        entity.FileStatusesJson = JsonSerializer.Serialize(status.Files ?? operation.FileStatuses);
        entity.Paused = status.Paused;
        entity.Completed = status.Completed;
        entity.Error = status.Error;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task DeletePersistedDownloadAsync(Guid downloadId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ModelDownloads.SingleOrDefaultAsync(download => download.Id == downloadId, cancellationToken);
        if (entity is null)
            return;

        db.ModelDownloads.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void DeleteDownloadFiles(DownloadOperation operation)
    {
        foreach (var fileName in operation.FileNames)
        {
            var path = GetDownloadFilePath(operation.Destination, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }

        if (!operation.Library.Equals("gguf", StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(operation.Destination) &&
            !Directory.EnumerateFileSystemEntries(operation.Destination).Any())
            Directory.Delete(operation.Destination);
    }

    private static ModelDownloadStatus CreateDownloadStatus(DownloadOperation operation, bool completed = false, bool queued = false)
    {
        var files = operation.FileStatuses;
        var bytesDownloaded = files.Sum(file => file.BytesDownloaded);
        long? totalBytes = files.All(file => file.TotalBytes is not null)
            ? files.Sum(file => file.TotalBytes!.Value)
            : null;
        return new ModelDownloadStatus(operation.Id, operation.ModelId,
            files.Count == 1 ? files[0].FileName : "Modelldateien",
            operation.Destination, bytesDownloaded, totalBytes, completed, null, false, queued, files);
    }

    private static void EnsureHuggingFaceSuccessStatusCode(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Hugging Face verweigert den Zugriff auf dieses Repository. Für private oder geschützte Modelle bitte ModelLibrary:HuggingFaceToken oder die Umgebungsvariable HF_TOKEN konfigurieren.");

        response.EnsureSuccessStatusCode();
    }

    private static string GetDownloadFilePath(string destination, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains('\0'))
            throw new InvalidOperationException("The Hugging Face file name is invalid.");

        var normalizedFileName = fileName.Replace('\\', '/');
        if (Path.IsPathRooted(normalizedFileName))
            throw new InvalidOperationException("The Hugging Face file path must be relative.");

        var root = Path.GetFullPath(destination);
        var path = Path.GetFullPath(Path.Combine(root, normalizedFileName.Replace('/', Path.DirectorySeparatorChar)));
        var relativePath = Path.GetRelativePath(root, path);
        if (relativePath is "." or ".." || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
            throw new InvalidOperationException("The Hugging Face file path is outside the model directory.");

        return path;
    }

    private Task PublishDownloadUpdateAsync(ModelDownloadStatus status, IReadOnlyList<LocalModelInfo>? localModels = null, bool cancelled = false, string eventName = "ModelDownload_Update")
    {
        var download = new DownloadStatus(status.Id, status.ModelId, status.FileName, status.DestinationPath,
            status.BytesDownloaded, status.TotalBytes, status.Completed, status.Error, status.Paused, status.Queued, status.Files);
        var models = localModels?.Select(model => new LocalModel(model.Name, model.Path, model.SizeInBytes, model.LastWriteTimeUtc, model.Format)).ToArray();
        return hubContext.Clients.All.SendAsync(eventName, new ModelDownloadUpdate(download, models, cancelled));
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
    public int MaxParallelDownloads { get; set; } = 3;
    public int MaxParallelFileDownloads { get; set; } = 2;
    public string? HuggingFaceToken { get; set; }
}

public sealed record LocalModelInfo(string Name, string Path, long SizeInBytes, DateTime LastWriteTimeUtc, ReferenceModelFormat Format = ReferenceModelFormat.Gguf);

public sealed record HuggingFaceModelInfo(
    string Id,
    string? Author,
    long Downloads,
    long Likes,
    DateTime? LastModified,
    [property: JsonPropertyName("library_name")] string? LibraryName = null,
    [property: JsonPropertyName("pipeline_tag")] string? PipelineTag = null,
    IReadOnlyList<string>? Tags = null);

internal sealed record RepositoryFile(string Name, long? SizeInBytes);

internal sealed record RepositorySnapshot(
    IReadOnlyList<RepositoryFile> Files,
    string Revision,
    string? LibraryName = null,
    string? PipelineTag = null,
    IReadOnlyList<string>? Tags = null);

public sealed record HuggingFaceRepositoryMetadata(
    string ModelId,
    string Revision,
    string? LibraryName,
    string? PipelineTag,
    IReadOnlyList<string> Tags);

public sealed record ModelDownloadStatus(Guid Id, string ModelId, string FileName, string DestinationPath, long BytesDownloaded, long? TotalBytes, bool Completed, string? Error, bool Paused = false, bool Queued = false, IReadOnlyList<DownloadFileStatus>? Files = null);

internal sealed class DownloadOperation(Guid id, string modelId, string library, IReadOnlyList<string> fileNames, string destination, string revision)
{
    private CancellationTokenSource cancellationTokenSource = new();
    private int cancelRequested;
    private int pauseRequested;

    public Guid Id { get; } = id;
    public string ModelId { get; } = modelId;
    public string Library { get; } = library;
    public IReadOnlyList<string> FileNames { get; } = fileNames;
    public string Destination { get; } = destination;
    public string Revision { get; } = revision;
    private TaskCompletionSource completion = CreateCompletionSource();
    private readonly ConcurrentDictionary<string, DownloadFileStatus> fileStatuses = new(StringComparer.OrdinalIgnoreCase);
    public Task Task => completion.Task;
    public TaskCompletionSource Completion => completion;
    public object SyncRoot { get; } = new();
    public IReadOnlyList<DownloadFileStatus> FileStatuses => fileStatuses.Values.OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase).ToArray();
    public bool PauseRequested => Volatile.Read(ref pauseRequested) != 0;
    public bool CancelRequested => Volatile.Read(ref cancelRequested) != 0;
    public CancellationToken Token => cancellationTokenSource.Token;
    private DateTimeOffset statusPublishedAtUtc;

    public void PrepareForQueue() => completion = CreateCompletionSource();

    public void InitializeFileStatuses(IReadOnlyList<string> filePaths, IReadOnlyList<DownloadFileStatus>? knownStatuses = null)
    {
        for (var index = 0; index < FileNames.Count; index++)
        {
            var fileName = FileNames[index];
            var path = filePaths[index];
            var bytesDownloaded = File.Exists(path) ? new FileInfo(path).Length : 0;
            var knownStatus = knownStatuses?.FirstOrDefault(status => status.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
            var completed = knownStatus?.TotalBytes is { } totalBytes && bytesDownloaded >= totalBytes;
            fileStatuses[fileName] = new DownloadFileStatus(fileName, bytesDownloaded, knownStatus?.TotalBytes, completed);
        }
    }

    public void SetFileStatus(DownloadFileStatus status) => fileStatuses[status.FileName] = status;

    public void RequestPause()
    {
        Volatile.Write(ref pauseRequested, 1);
        cancellationTokenSource.Cancel();
    }

    public void RequestCancel()
    {
        Volatile.Write(ref cancelRequested, 1);
        cancellationTokenSource.Cancel();
    }

    public void Resume()
    {
        cancellationTokenSource = new CancellationTokenSource();
        Volatile.Write(ref pauseRequested, 0);
    }

    public bool ShouldPublishStatus(DateTimeOffset now)
    {
        if (statusPublishedAtUtc != default && now - statusPublishedAtUtc < TimeSpan.FromSeconds(1))
            return false;

        statusPublishedAtUtc = now;
        return true;
    }

    public void MarkStatusPublished(DateTimeOffset now) => statusPublishedAtUtc = now;

    private static TaskCompletionSource CreateCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
