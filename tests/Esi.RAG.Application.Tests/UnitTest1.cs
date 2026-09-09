using Esi.RAG.Application;
using Esi.RAG.Domain;

namespace Esi.RAG.Application.Tests;

public class UnitTest1
{
    [Fact]
    public void SearchRequest_DefaultLimit_ShouldBeFive()
    {
        var request = new SearchRequest("find symbols");
        Assert.Equal(5, request.Limit);
    }

    [Fact]
    public void RankAndDeduplicate_RemovesDuplicateContentAndFavorsCode()
    {
        var codeChunk = MakeChunk("a.cs", "csharp", "hash-1");
        var duplicateChunk = MakeChunk("a.cs", "csharp", "hash-1");
        var markdownChunk = MakeChunk("b.md", "markdown", "hash-2");

        var matches = new[]
        {
            new RetrievedPassage(markdownChunk, 0.9f),
            new RetrievedPassage(codeChunk, 0.85f),
            new RetrievedPassage(duplicateChunk, 0.85f),
        };

        var citations = SearchService.RankAndDeduplicate(matches, limit: 5);

        Assert.Equal(2, citations.Count);
        Assert.Equal("a.cs", citations[0].RelativePath);
    }

    [Fact]
    public void RankAndDeduplicate_LexicalTermsFavorAuthoritativeTenantMapping()
    {
        var mapping = MakeChunk(
            "Mappings\\TenantMapInt.cs",
            "csharp",
            "mapping",
            """modelBuilder.Entity<Tenant<int>>().Property(e => e.Name).HasColumnName("MndName");""");
        var unrelated = MakeChunk("docs\\tenant.md", "markdown", "unrelated", "Tenant names are configured elsewhere.");

        var citations = SearchService.RankAndDeduplicate(
            [
                new RetrievedPassage(unrelated, 0.92f),
                new RetrievedPassage(mapping, 0.82f),
            ],
            limit: 1,
            query: "Tenant<int>.Name MndName");

        Assert.Single(citations);
        Assert.Equal("Mappings\\TenantMapInt.cs", citations[0].RelativePath);
        Assert.Contains("HasColumnName", citations[0].Excerpt);
    }

    private static DocumentChunk MakeChunk(
        string relativePath,
        string language,
        string contentHash,
        string content = "content") => new(
        Guid.NewGuid().ToString(),
        "repo",
        "deadbeef",
        relativePath,
        relativePath,
        language,
        "symbol",
        content,
        contentHash,
        1,
        2,
        0,
        [1f, 0f]);
}
