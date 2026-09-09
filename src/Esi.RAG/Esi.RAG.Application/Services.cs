using System.Text;
using System.Text.RegularExpressions;
using Esi.RAG.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Esi.RAG.Application;

/// <summary>
/// Retrieves candidate chunks, removes duplicate evidence, applies a simple authority ranking
/// (favoring code and SQL definitions over prose), and asks the text-generation client to answer
/// strictly from the retained evidence.
/// </summary>
public sealed class SearchService(
    IEmbeddingClient embeddingClient,
    IVectorStore vectorStore,
    ITextGenerationClient textGenerationClient) : ISearchService
{
    private static readonly Regex QueryTermRegex = new(@"\b[A-Za-z_][A-Za-z0-9_]*\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, float> LanguageAuthority = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
    {
        ["csharp"] = 1.00f,
        ["sql"] = 0.95f,
        ["markdown"] = 0.75f,
        ["json"] = 0.65f,
        ["yaml"] = 0.65f,
        ["xml"] = 0.60f,
    };

    public async Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, 50);
        var queryEmbedding = await embeddingClient.CreateEmbeddingAsync(request.Query, cancellationToken).ConfigureAwait(false);
        var overFetch = Math.Min(200, Math.Max(limit + 8, limit * 8));
        var matches = await vectorStore.SearchAsync(queryEmbedding, overFetch, request.Filter, cancellationToken).ConfigureAwait(false);

        var citations = RankAndDeduplicate(matches, limit, request.Query);
        var context = BuildContext(citations);

        var answer = await textGenerationClient.CompleteAsync(
                "You are a retrieval assistant. Answer only from provided evidence. If the evidence is insufficient, say so explicitly.",
                $"Question:{Environment.NewLine}{request.Query}{Environment.NewLine}{Environment.NewLine}Evidence:{Environment.NewLine}{context}",
                cancellationToken)
            .ConfigureAwait(false);

        if (answer.StartsWith("No local model response available", StringComparison.OrdinalIgnoreCase))
        {
            answer = citations.Count == 0
                ? "No matching citations were found in the indexed content."
                : $"Found {citations.Count} citation(s). Top evidence:{Environment.NewLine}{context}";
        }

        return new SearchResult(answer, citations);
    }

    internal static IReadOnlyList<Citation> RankAndDeduplicate(
        IReadOnlyList<RetrievedPassage> matches,
        int limit,
        string? query = null)
    {
        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranked = new List<(RetrievedPassage Match, float Rank)>();
        var queryTerms = ExtractQueryTerms(query);

        foreach (var match in matches)
        {
            if (!seenHashes.Add(match.Chunk.ContentHash))
            {
                continue;
            }

            var authority = LanguageAuthority.TryGetValue(match.Chunk.Language, out var weight) ? weight : 0.5f;
            var lexicalBoost = LexicalBoost(match.Chunk, queryTerms);
            ranked.Add((match, match.Score * authority + lexicalBoost));
        }

        return ranked
            .OrderByDescending(entry => entry.Rank)
            .Take(Math.Max(1, limit))
            .Select(entry => ToCitation(entry.Match))
            .ToArray();
    }

    private static IReadOnlySet<string> ExtractQueryTerms(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return QueryTermRegex.Matches(query)
            .Select(match => match.Value)
            .Where(term => term.Length >= 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static float LexicalBoost(DocumentChunk chunk, IReadOnlySet<string> queryTerms)
    {
        if (queryTerms.Count == 0)
        {
            return 0f;
        }

        var searchableText = $"{chunk.Content} {chunk.Symbol} {chunk.RelativePath}";
        var matchedTerms = queryTerms.Count(term =>
            Regex.IsMatch(searchableText, $@"(?<![A-Za-z0-9_]){Regex.Escape(term)}(?![A-Za-z0-9_])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

        return Math.Min(0.60f, matchedTerms * 0.12f);
    }

    internal static Citation ToCitation(RetrievedPassage match)
    {
        var chunk = match.Chunk;
        var excerpt = chunk.Content.Length > 280 ? $"{chunk.Content[..280]}..." : chunk.Content;
        return new Citation(
            chunk.FilePath,
            chunk.StartLine,
            chunk.EndLine,
            excerpt,
            chunk.RelativePath,
            chunk.Symbol,
            chunk.Language,
            match.Score,
            chunk.CommitSha);
    }

    internal static string BuildContext(IReadOnlyList<Citation> citations) => string.Join(
        Environment.NewLine + Environment.NewLine,
        citations.Select(citation => $"{citation.RelativePath} [{citation.StartLine}-{citation.EndLine}] ({citation.Symbol})" +
            $"{Environment.NewLine}{citation.Excerpt}"));
}

/// <summary>
/// A bounded, evidence-only investigation agent. Its only tool is repository search (no code
/// execution, no file writes); it stops after a fixed number of rounds or once no new evidence is
/// found, and answers exclusively from the accumulated citations.
/// </summary>
public sealed class InvestigationAgent(
    ISearchService searchService,
    ITextGenerationClient textGenerationClient) : IAskService
{
    private const string SearchTool = "repository_search";

    public async Task<InvestigationResult> AskAsync(AskRequest request, CancellationToken cancellationToken)
    {
        var maxRounds = Math.Clamp(request.MaxRounds, 1, 8);
        var maxCitations = Math.Clamp(request.MaxCitations, 1, 40);
        var plan = new InvestigationPlan(
            request.Query,
            Enumerable.Range(1, maxRounds).Select(round => $"Round {round}: search and refine evidence for '{request.Query}'").ToArray());

        var steps = new List<InvestigationStep>();
        var uniqueCitationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allCitations = new List<Citation>();
        var currentPrompt = request.Query;

        for (var round = 1; round <= maxRounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var searchResult = await searchService
                .SearchAsync(new SearchRequest(currentPrompt, Math.Min(6, maxCitations), request.Filter), cancellationToken)
                .ConfigureAwait(false);

            var remaining = Math.Max(0, maxCitations - allCitations.Count);
            var newCitations = searchResult.Citations
                .Where(citation => uniqueCitationKeys.Add($"{citation.RelativePath}:{citation.StartLine}:{citation.EndLine}"))
                .Take(remaining)
                .ToArray();
            allCitations.AddRange(newCitations);

            steps.Add(new InvestigationStep(
                round,
                $"Search evidence for: {currentPrompt}",
                SearchTool,
                currentPrompt,
                searchResult.Answer,
                newCitations,
                Success: true));

            if (allCitations.Count >= maxCitations || newCitations.Length == 0)
            {
                break;
            }

            currentPrompt = $"Refine answer for: {request.Query}. Previous finding: {searchResult.Answer}";
        }

        var evidenceSufficient = allCitations.Count > 0;
        var evidence = new StringBuilder();
        foreach (var step in steps)
        {
            evidence.AppendLine($"Prompt: {step.ToolInput}");
            evidence.AppendLine($"Finding: {step.Output}");
            foreach (var citation in step.Citations)
            {
                evidence.AppendLine($"- {citation.RelativePath} [{citation.StartLine}-{citation.EndLine}] ({citation.Symbol})");
            }

            evidence.AppendLine();
        }

        var answer = await textGenerationClient.CompleteAsync(
                "Answer strictly from the given evidence and cite file paths with line ranges. If evidence is weak or absent, say so explicitly.",
                $"Question: {request.Query}{Environment.NewLine}{Environment.NewLine}Evidence:{Environment.NewLine}{evidence}",
                cancellationToken)
            .ConfigureAwait(false);

        if (answer.StartsWith("No local model response available", StringComparison.OrdinalIgnoreCase))
        {
            answer = evidenceSufficient
                ? "Bounded investigation completed in extractive mode only. Review cited snippets."
                : "No evidence was found for this query within the bounded investigation.";
        }

        return new InvestigationResult(request.Query, steps, allCitations, answer, evidenceSufficient);
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddRagApplication(this IServiceCollection services)
    {
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<IAskService, InvestigationAgent>();
        return services;
    }
}
