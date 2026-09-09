using Esi.RAG.Domain;

namespace Esi.RAG.Domain.Tests;

public class UnitTest1
{
    [Fact]
    public void Citation_Should_Keep_Line_Bounds()
    {
        var citation = new Citation("repo\\file.cs", 10, 20, "snippet");
        Assert.Equal(10, citation.StartLine);
        Assert.Equal(20, citation.EndLine);
    }
}
