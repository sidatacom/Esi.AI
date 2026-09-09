using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Esi.RAG.Application;
using Esi.RAG.Domain;
using Microsoft.Extensions.Options;

namespace Esi.RAG.Infrastructure;

/// <summary>
/// Reads repository identity metadata (branch, commit sha, remote url) directly from the
/// .git directory. This never shells out to git.exe and never executes anything from the
/// repository being indexed - it only parses plain-text ref/config files.
/// </summary>
public interface IGitMetadataReader
{
    ProjectMetadata ReadMetadata(string repositoryPath);
}

public sealed class GitMetadataReader : IGitMetadataReader
{
    public ProjectMetadata ReadMetadata(string repositoryPath)
    {
        var normalizedRoot = string.IsNullOrWhiteSpace(repositoryPath) ? "." : repositoryPath;
        var repositoryName = new DirectoryInfo(normalizedRoot).Name;
        var gitDir = FindGitDirectory(normalizedRoot);

        string? branch = null;
        string? commitSha = null;
        string? remoteUrl = null;

        if (gitDir is not null)
        {
            try
            {
                (branch, commitSha) = ReadHead(gitDir);
                remoteUrl = ReadRemoteUrl(gitDir);
            }
            catch
            {
                // Best-effort only: metadata reading must never fail ingestion.
            }
        }

        return new ProjectMetadata(normalizedRoot, repositoryName, branch, commitSha, remoteUrl, DateTimeOffset.UtcNow);
    }

    private static string? FindGitDirectory(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            if (File.Exists(candidate))
            {
                // Worktree/submodule pointer file: "gitdir: <path>"
                var pointerLine = File.ReadAllText(candidate).Trim();
                var prefix = "gitdir:";
                if (pointerLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var target = pointerLine[prefix.Length..].Trim();
                    return Path.IsPathRooted(target) ? target : Path.GetFullPath(Path.Combine(current.FullName, target));
                }
            }

            current = current.Parent;
        }

        return null;
    }

    private static (string? Branch, string? CommitSha) ReadHead(string gitDir)
    {
        var headPath = Path.Combine(gitDir, "HEAD");
        if (!File.Exists(headPath))
        {
            return (null, null);
        }

        var head = File.ReadAllText(headPath).Trim();
        const string refPrefix = "ref:";
        if (!head.StartsWith(refPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Detached HEAD: HEAD contains the commit sha directly.
            return (null, head);
        }

        var refName = head[refPrefix.Length..].Trim();
        var branch = refName.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase)
            ? refName["refs/heads/".Length..]
            : refName;

        var refPath = Path.Combine(gitDir, refName.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(refPath))
        {
            return (branch, File.ReadAllText(refPath).Trim());
        }

        var packedRefsPath = Path.Combine(gitDir, "packed-refs");
        if (File.Exists(packedRefsPath))
        {
            foreach (var line in File.ReadLines(packedRefsPath))
            {
                if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = line.Split(' ', 2);
                if (parts.Length == 2 && parts[1].Trim() == refName)
                {
                    return (branch, parts[0].Trim());
                }
            }
        }

        return (branch, null);
    }

    private static string? ReadRemoteUrl(string gitDir)
    {
        var configPath = Path.Combine(gitDir, "config");
        if (!File.Exists(configPath))
        {
            return null;
        }

        var inOriginSection = false;
        foreach (var rawLine in File.ReadLines(configPath))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inOriginSection = line.Equals("[remote \"origin\"]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inOriginSection && line.StartsWith("url", StringComparison.OrdinalIgnoreCase))
            {
                var separatorIndex = line.IndexOf('=');
                if (separatorIndex > 0)
                {
                    return line[(separatorIndex + 1)..].Trim();
                }
            }
        }

        return null;
    }
}

/// <summary>
/// Walks the repository tree, applying configurable size/secret/generated-content exclusions
/// before returning a list of documents eligible for extraction.
/// </summary>
public sealed class FileDiscoveryService(IOptions<RagOptions> options, IGitMetadataReader gitMetadataReader) : IRepositoryDiscovery
{
    private readonly RagOptions _options = options.Value;

    public Task<RepositoryDiscoveryResult> DiscoverAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var root = string.IsNullOrWhiteSpace(repositoryPath) ? _options.RepositoryPath : repositoryPath;
        var project = gitMetadataReader.ReadMetadata(root);

        if (!Directory.Exists(root))
        {
            return Task.FromResult(new RepositoryDiscoveryResult([], [], project));
        }

        var excludedDirs = new HashSet<string>(_options.ExcludedDirectories, StringComparer.OrdinalIgnoreCase);
        var allowedExt = new HashSet<string>(_options.IncludedExtensions, StringComparer.OrdinalIgnoreCase);
        var secretContentPatterns = _options.SecretContentPatterns
            .Select(pattern => new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant))
            .ToArray();

        var documents = new List<RepositoryDocument>();
        var skipped = new List<IngestionIssue>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = pending.Pop();
            IEnumerable<string> directories;
            IEnumerable<string> files;
            try
            {
                directories = Directory.EnumerateDirectories(dir);
                files = Directory.EnumerateFiles(dir);
            }
            catch
            {
                continue;
            }

            foreach (var child in directories)
            {
                if (!excludedDirs.Contains(Path.GetFileName(child)))
                {
                    pending.Push(child);
                }
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var extension = Path.GetExtension(file);
                if (!allowedExt.Contains(extension))
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(root, file);
                var fileName = Path.GetFileName(file);

                if (MatchesAnyGlob(fileName, _options.SecretFileNamePatterns) || MatchesAnyGlob(relativePath, _options.SecretFileNamePatterns))
                {
                    skipped.Add(new IngestionIssue(relativePath, "excluded:secret-filename"));
                    continue;
                }

                if (MatchesAnyGlob(fileName, _options.GeneratedPathPatterns))
                {
                    skipped.Add(new IngestionIssue(relativePath, "excluded:generated-filename"));
                    continue;
                }

                FileInfo info;
                try
                {
                    info = new FileInfo(file);
                }
                catch
                {
                    skipped.Add(new IngestionIssue(relativePath, "excluded:unreadable"));
                    continue;
                }

                if (info.Length > _options.MaxFileSizeBytes)
                {
                    skipped.Add(new IngestionIssue(relativePath, $"excluded:too-large:{info.Length}bytes"));
                    continue;
                }

                string content;
                try
                {
                    content = File.ReadAllText(file);
                }
                catch
                {
                    skipped.Add(new IngestionIssue(relativePath, "excluded:unreadable"));
                    continue;
                }

                if (ContainsGeneratedMarker(content, _options.GeneratedContentMarkers))
                {
                    skipped.Add(new IngestionIssue(relativePath, "excluded:generated-content"));
                    continue;
                }

                if (secretContentPatterns.Any(pattern => pattern.IsMatch(content)))
                {
                    skipped.Add(new IngestionIssue(relativePath, "excluded:secret-content"));
                    continue;
                }

                var language = LanguageFromExtension(extension);
                var contentHash = ComputeHash(content);
                documents.Add(new RepositoryDocument(
                    file,
                    relativePath,
                    extension,
                    language,
                    info.Length,
                    contentHash,
                    info.LastWriteTimeUtc,
                    IsGenerated: false));
            }
        }

        return Task.FromResult(new RepositoryDiscoveryResult(documents, skipped, project));
    }

    private static bool ContainsGeneratedMarker(string content, IReadOnlyCollection<string> markers)
    {
        if (markers.Count == 0)
        {
            return false;
        }

        var head = content.Length > 400 ? content[..400] : content;
        return markers.Any(marker => head.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesAnyGlob(string value, IReadOnlyCollection<string> patterns)
        => patterns.Any(pattern => IsGlobMatch(value, pattern));

    private static bool IsGlobMatch(string value, string globPattern)
    {
        var regexPattern = "^" + Regex.Escape(globPattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, regexPattern, RegexOptions.IgnoreCase);
    }

    internal static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    internal static string LanguageFromExtension(string extension) => extension.TrimStart('.').ToLowerInvariant() switch
    {
        "cs" or "csproj" or "sln" => "csharp",
        "sql" => "sql",
        "md" or "markdown" => "markdown",
        "js" or "jsx" => "javascript",
        "ts" or "tsx" => "typescript",
        "json" => "json",
        "yml" or "yaml" => "yaml",
        "xml" or "config" => "xml",
        "html" or "htm" => "html",
        "css" or "scss" => "css",
        "cshtml" or "razor" => "razor",
        "vb" => "visualbasic",
        "fs" => "fsharp",
        var other => other.Length == 0 ? "text" : other,
    };
}
