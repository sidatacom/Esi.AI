using Esi.AI.Models;
using YamlDotNet.RepresentationModel;

namespace Esi.AI.Studio.Services;

/// <summary>Scans configured directories for locally available model formats.</summary>
public interface ILocalModelScanner
{
    /// <summary>Finds configured YAML, GGUF, OpenVINO, and Transformers models.</summary>
    Task<IReadOnlyList<LocalModelInfo>> ScanAsync(
        IReadOnlyList<string> directories,
        CancellationToken cancellationToken = default);
}

/// <summary>Filesystem-only model scanner with no persistence or transport dependencies.</summary>
public sealed class LocalModelScanner : ILocalModelScanner
{
    /// <inheritdoc />
    public Task<IReadOnlyList<LocalModelInfo>> ScanAsync(
        IReadOnlyList<string> directories,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directories);
        cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(directory))
                continue;

            foreach (var path in Directory.EnumerateFiles(directory, "*.gguf", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = new FileInfo(path);
                if (!referencedFiles.Contains(file.FullName))
                    models.TryAdd(file.FullName, new LocalModelInfo(file.Name, file.FullName, file.Length, file.LastWriteTimeUtc));
            }

            foreach (var model in ScanOpenVinoModels(directory, cancellationToken))
                models.TryAdd(model.Path, model);

            foreach (var model in ScanTransformersModels(directory, cancellationToken))
                models.TryAdd(model.Path, model);
        }

        IReadOnlyList<LocalModelInfo> result = models
            .Values
            .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(result);
    }

    private static IReadOnlyList<LocalModelInfo> ScanOpenVinoModels(string directory, CancellationToken cancellationToken)
    {
        var models = new Dictionary<string, LocalModelInfo>(StringComparer.OrdinalIgnoreCase);
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

        return models.Values.OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<LocalModelInfo> ScanTransformersModels(string directory, CancellationToken cancellationToken)
    {
        var models = new Dictionary<string, LocalModelInfo>(StringComparer.OrdinalIgnoreCase);
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
}

/// <summary>Describes a model discovered on the local filesystem.</summary>
public sealed record LocalModelInfo(
    string Name,
    string Path,
    long SizeInBytes,
    DateTime LastWriteTimeUtc,
    ReferenceModelFormat Format = ReferenceModelFormat.Gguf);