using Esi.RAG.Application;
using Esi.RAG.Domain;
using Esi.RAG.Infrastructure;
using Microsoft.Extensions.Options;

namespace Esi.RAG.Infrastructure.Tests;

public class UnitTest1
{
    [Fact]
    public void Chunker_Should_Preserve_LineNumbers()
    {
        var options = Options.Create(new RagOptions { ChunkMaxLines = 10, ChunkOverlapLines = 1 });
        var chunker = new LinePreservingChunker(options);
        var segment = new SourceSegment("x.cs", "csharp", "method:X", "1\n2\n3\n4\n5\n6\n7\n8\n9\n10\n11", 11, 21);
        var extraction = new ExtractionResult("x.cs", "csharp", [segment]);
        var chunks = chunker.Chunk(extraction);

        Assert.Equal(2, chunks.Count);
        Assert.Equal((11, 20), (chunks[0].StartLine, chunks[0].EndLine));
        Assert.Equal((20, 21), (chunks[1].StartLine, chunks[1].EndLine));
    }

    [Fact]
    public async Task FileDiscovery_Should_Exclude_Configured_Directories()
    {
        var root = Path.Combine(AppContext.BaseDirectory, $"discovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(Path.Combine(root, ".git", "ignored.cs"), "class Ignored {}");
        await File.WriteAllTextAsync(Path.Combine(root, "src", "kept.cs"), "class Kept {}");
        var options = Options.Create(new RagOptions());
        var discovery = new FileDiscoveryService(options, new GitMetadataReader());

        try
        {
            var result = await discovery.DiscoverAsync(root, CancellationToken.None);
            Assert.Single(result.Documents);
            Assert.Contains("kept.cs", result.Documents[0].RelativePath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task FileDiscovery_Should_Skip_Files_With_Secret_Content()
    {
        var root = Path.Combine(AppContext.BaseDirectory, $"discovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "secret.cs"), "// password = \"hunter2000\"\nclass X {}");
        var options = Options.Create(new RagOptions());
        var discovery = new FileDiscoveryService(options, new GitMetadataReader());

        try
        {
            var result = await discovery.DiscoverAsync(root, CancellationToken.None);
            Assert.Empty(result.Documents);
            Assert.Contains(result.Skipped, issue => issue.Reason.Contains("secret", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GitMetadataReader_ReadsBranchAndCommit_FromPlainGitDirectory()
    {
        var root = Path.Combine(AppContext.BaseDirectory, $"gitrepo-{Guid.NewGuid():N}");
        var gitDir = Path.Combine(root, ".git");
        Directory.CreateDirectory(Path.Combine(gitDir, "refs", "heads"));
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(Path.Combine(gitDir, "refs", "heads", "main"), "abc123def456\n");
        File.WriteAllText(Path.Combine(gitDir, "config"),
            "[core]\n\trepositoryformatversion = 0\n[remote \"origin\"]\n\turl = https://example.test/repo.git\n\tfetch = +refs/heads/*:refs/remotes/origin/*\n");

        try
        {
            var metadata = new GitMetadataReader().ReadMetadata(root);
            Assert.Equal("main", metadata.GitBranch);
            Assert.Equal("abc123def456", metadata.GitCommitSha);
            Assert.Equal("https://example.test/repo.git", metadata.GitRemoteUrl);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}


