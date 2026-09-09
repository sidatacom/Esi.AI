using Esi.RAG.Infrastructure;
namespace Esi.RAG.Infrastructure.Tests;

public sealed class ExtractionTests
{
    [Fact]
    public async Task CSharpExtractor_ReturnsSymbolsAndOriginalLines()
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"sample-{Guid.NewGuid():N}.cs");
        await File.WriteAllTextAsync(path, "class Sample\n{\n    void Run()\n    {\n    }\n}\n");

        try
        {
            var result = await new CompositeSourceExtractor().ExtractAsync(path, CancellationToken.None);
            Assert.Contains(result.Segments, segment => segment.Symbol == "class:Sample" && segment.StartLine == 1);
            Assert.Contains(result.Segments, segment => segment.Symbol == "method:Sample.Run" && segment.StartLine == 3);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CSharpExtractor_CollectsCodeSymbolsWithContainingTypeAndNamespace()
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"sample-{Guid.NewGuid():N}.cs");
        await File.WriteAllTextAsync(path, "namespace Demo.App;\n\nclass Sample\n{\n    void Run()\n    {\n    }\n}\n");

        try
        {
            var result = await new CompositeSourceExtractor().ExtractAsync(path, CancellationToken.None);
            Assert.NotNull(result.Symbols);
            Assert.Contains(result.Symbols!, symbol => symbol is { Kind: "class", Name: "Sample", Namespace: "Demo.App" });
            Assert.Contains(result.Symbols!, symbol => symbol is { Kind: "method", Name: "Run", ContainingType: "Sample", Namespace: "Demo.App" });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CSharpExtractor_EmitsSpecializedEfMappingEvidenceWithOriginalLines()
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"sample-{Guid.NewGuid():N}.cs");
        await File.WriteAllTextAsync(path, """
            modelBuilder.Entity<Tenant<int>>()
                .Property(e => e.Name)
                .HasColumnName("MndName");
            """);

        try
        {
            var result = await new CompositeSourceExtractor().ExtractAsync(path, CancellationToken.None);
            var mapping = Assert.Single(result.Segments, segment => segment.Symbol.StartsWith("ef-mapping:", StringComparison.Ordinal));
            Assert.Contains("Property(e => e.Name)", mapping.Content);
            Assert.Contains("HasColumnName(\"MndName\")", mapping.Content);
            Assert.Equal(1, mapping.StartLine);
            Assert.Equal(3, mapping.EndLine);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CSharpExtractor_FallsBackToWholeFile_OnSyntaxErrors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"sample-{Guid.NewGuid():N}.cs");
        await File.WriteAllTextAsync(path, "class Broken { void Run( {");

        try
        {
            var result = await new CompositeSourceExtractor().ExtractAsync(path, CancellationToken.None);
            Assert.Single(result.Segments);
            Assert.Equal("syntax-fallback", result.Segments[0].Symbol);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SqlExtractor_PreservesStatementLineRanges()
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"sample-{Guid.NewGuid():N}.sql");
        await File.WriteAllTextAsync(path, "-- header\nselect * from Users;\n\nupdate Users set Name = 'x';\n");

        try
        {
            var result = await new CompositeSourceExtractor().ExtractAsync(path, CancellationToken.None);
            Assert.Equal(2, result.Segments.Count);
            Assert.Equal("sql:select", result.Segments[0].Symbol);
            Assert.Equal(2, result.Segments[0].StartLine);
            Assert.Equal("sql:update", result.Segments[1].Symbol);
            Assert.Equal(4, result.Segments[1].StartLine);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SqlExtractor_CollectsSqlObjectsForCreateStatements()
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"sample-{Guid.NewGuid():N}.sql");
        await File.WriteAllTextAsync(path, "CREATE TABLE dbo.Users (Id INT NOT NULL);\nCREATE PROCEDURE dbo.GetUsers AS SELECT 1;");

        try
        {
            var result = await new CompositeSourceExtractor().ExtractAsync(path, CancellationToken.None);
            Assert.NotNull(result.SqlObjects);
            Assert.Contains(result.SqlObjects!, obj => obj is { ObjectType: "table", Name: "Users", Schema: "dbo" });
            Assert.Contains(result.SqlObjects!, obj => obj is { ObjectType: "procedure", Name: "GetUsers", Schema: "dbo" });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MarkdownExtractor_PreservesHeadingLineRanges()
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"sample-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(path, "# Title\nLine A\n## Section\nLine B\n");

        try
        {
            var result = await new CompositeSourceExtractor().ExtractAsync(path, CancellationToken.None);
            Assert.Equal("markdown", result.Language);
            Assert.Contains(result.Segments, segment => segment.Symbol == "markdown:section:Title" && segment.StartLine == 1);
            Assert.Contains(result.Segments, segment => segment.Symbol == "markdown:section:Section" && segment.StartLine == 3);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
