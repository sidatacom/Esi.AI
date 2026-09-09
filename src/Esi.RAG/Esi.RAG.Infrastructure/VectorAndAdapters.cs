using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Esi.RAG.Application;
using Esi.RAG.Domain;
using Microsoft.Extensions.Options;

namespace Esi.RAG.Infrastructure;

/// <summary>Deterministic, dependency-free vector store used as a fallback and in tests.</summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, DocumentChunk> _index = new(StringComparer.Ordinal);

    public Task UpsertAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _index[chunk.Id] = chunk;
        }

        return Task.CompletedTask;
    }

    public Task<FileIndexMetadata?> GetFileIndexMetadataAsync(string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var chunk = _index.Values.FirstOrDefault(value => string.Equals(value.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (chunk is null)
        {
            return Task.FromResult<FileIndexMetadata?>(null);
        }

        return Task.FromResult<FileIndexMetadata?>(new FileIndexMetadata(
            chunk.RelativePath,
            chunk.CommitSha,
            chunk.FileContentHash,
            chunk.EmbeddingModel,
            chunk.IngestionFingerprint,
            chunk.FileChunkCount));
    }

    public Task<IReadOnlyList<RetrievedPassage>> SearchAsync(IReadOnlyList<float> queryEmbedding, int limit, SearchFilter? filter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = _index.Values
            .Where(chunk => MatchesFilter(chunk, filter))
            .Select(chunk => new RetrievedPassage(chunk, CosineSimilarity(queryEmbedding, chunk.Embedding)))
            .OrderByDescending(match => match.Score)
            .Take(Math.Max(1, limit))
            .ToArray();

        return Task.FromResult<IReadOnlyList<RetrievedPassage>>(results);
    }

    public Task<int> DeleteByFilePathAsync(string relativePath, CancellationToken cancellationToken)
    {
        var keys = _index.Values
            .Where(chunk => string.Equals(chunk.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
            .Select(chunk => chunk.Id)
            .ToArray();

        foreach (var key in keys)
        {
            _index.TryRemove(key, out _);
        }

        return Task.FromResult(keys.Length);
    }

    internal static bool MatchesFilter(DocumentChunk chunk, SearchFilter? filter)
    {
        if (filter is null)
        {
            return true;
        }

        if (filter.Languages is { Count: > 0 } languages &&
            !languages.Contains(chunk.Language, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (filter.PathPrefixes is { Count: > 0 } pathPrefixes &&
            !pathPrefixes.Any(prefix => chunk.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (filter.SymbolPrefixes is { Count: > 0 } symbolPrefixes &&
            !symbolPrefixes.Any(prefix => chunk.Symbol.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private static float CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        var length = Math.Min(left.Count, right.Count);
        if (length == 0)
        {
            return 0f;
        }

        double dot = 0;
        double magLeft = 0;
        double magRight = 0;
        for (var i = 0; i < length; i++)
        {
            dot += left[i] * right[i];
            magLeft += left[i] * left[i];
            magRight += right[i] * right[i];
        }

        if (magLeft == 0 || magRight == 0)
        {
            return 0f;
        }

        return (float)(dot / (Math.Sqrt(magLeft) * Math.Sqrt(magRight)));
    }
}

/// <summary>Typed LM Studio HTTP client with retries, timeouts, and a health probe.</summary>
public sealed class LmStudioClient(
    IHttpClientFactory httpClientFactory,
    IOptions<LmStudioOptions> options) : IEmbeddingClient, ITextGenerationClient
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient(nameof(LmStudioClient));
    private readonly LmStudioOptions _options = options.Value;

    public string ModelName => _options.EmbeddingModel;

    public async Task<IReadOnlyList<float>> CreateEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        var request = new EmbeddingRequest(_options.EmbeddingModel, text);
        var payload = await SendWithRetryAsync(
                () => _httpClient.PostAsJsonAsync("/v1/embeddings", request, cancellationToken),
                async response => await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: cancellationToken).ConfigureAwait(false),
                cancellationToken)
            .ConfigureAwait(false);

        var vector = payload?.Data?.FirstOrDefault()?.Embedding;
        return vector is { Count: > 0 } ? vector : CreateDeterministicEmbedding(text);
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var request = new ChatRequest(
            _options.ChatModel,
            [new ChatMessage("system", systemPrompt), new ChatMessage("user", userPrompt)],
            0.2f);

        var payload = await SendWithRetryAsync(
                () => _httpClient.PostAsJsonAsync("/v1/chat/completions", request, cancellationToken),
                async response => await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: cancellationToken).ConfigureAwait(false),
                cancellationToken)
            .ConfigureAwait(false);

        var text = payload?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
        return string.IsNullOrWhiteSpace(text)
            ? "No local model response available. Falling back to citation-first extractive summary."
            : text;
    }

    public async Task<ComponentHealthResult> CheckHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/v1/models", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new ComponentHealthResult("lmstudio", true, $"{_options.BaseUrl} reachable")
                : new ComponentHealthResult("lmstudio", false, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return new ComponentHealthResult("lmstudio", false, ex.Message);
        }
    }

    private async Task<TResponse?> SendWithRetryAsync<TResponse>(
        Func<Task<HttpResponseMessage>> send,
        Func<HttpResponseMessage, Task<TResponse?>> readResponse,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var attempts = Math.Max(1, _options.RetryCount + 1);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using var response = await send().ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await readResponse(response).ConfigureAwait(false);
            }
            catch (Exception) when (attempt < attempts)
            {
                await Task.Delay(_options.RetryDelayMilliseconds * attempt, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static IReadOnlyList<float> CreateDeterministicEmbedding(string text)
    {
        var vector = new float[64];
        var tokens = text.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            var hash = token.GetHashCode(StringComparison.OrdinalIgnoreCase);
            var index = Math.Abs(hash) % vector.Length;
            vector[index] += 1f;
        }

        var magnitude = Math.Sqrt(vector.Sum(v => v * v));
        if (magnitude > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] = (float)(vector[i] / magnitude);
            }
        }

        return vector;
    }

    private sealed record EmbeddingRequest(string Model, string Input);
    private sealed record EmbeddingResponse(List<EmbeddingData>? Data);
    private sealed record EmbeddingData(List<float> Embedding);
    private sealed record ChatRequest(string Model, IReadOnlyList<ChatMessage> Messages, float Temperature);
    private sealed record ChatResponse(List<ChatChoice>? Choices);
    private sealed record ChatChoice(ChatMessage? Message);
    private sealed record ChatMessage(string Role, string Content);
}

/// <summary>
/// Real Qdrant HTTP adapter: ensures the collection exists, upserts payload-bearing points,
/// supports payload filtering on search, and supports delete-by-path (e.g. for re-ingestion).
/// Falls back to an in-memory store whenever Qdrant is unavailable and fallback is enabled.
/// </summary>
public sealed class QdrantVectorStoreAdapter(
    IHttpClientFactory httpClientFactory,
    IOptions<QdrantOptions> options,
    InMemoryVectorStore inMemoryVectorStore) : IVectorStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient = httpClientFactory.CreateClient(nameof(QdrantVectorStoreAdapter));
    private readonly QdrantOptions _options = options.Value;

    public async Task UpsertAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        await inMemoryVectorStore.UpsertAsync(chunks, cancellationToken).ConfigureAwait(false);
        if (chunks.Count == 0)
        {
            return;
        }

        await EnsureCollectionAsync(chunks[0].Embedding.Count, cancellationToken).ConfigureAwait(false);

        var points = chunks.Select(chunk => new QdrantPoint(
            chunk.Id,
            chunk.Embedding,
            new QdrantPayload(
                chunk.RepositoryName,
                chunk.CommitSha,
                chunk.FilePath,
                chunk.RelativePath,
                chunk.Language,
                chunk.Symbol,
                chunk.Content,
                chunk.ContentHash,
                chunk.StartLine,
                chunk.EndLine,
                chunk.ChunkIndex,
                chunk.EmbeddingModel,
                chunk.IngestionFingerprint,
                chunk.FileChunkCount,
                chunk.FileContentHash)));

        await ExecuteWithRetryAsync(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"/collections/{_options.CollectionName}/points?wait=true")
            {
                Content = JsonContent.Create(new QdrantUpsertRequest(points.ToArray()), options: SerializerOptions)
            };
            AddApiKey(request);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FileIndexMetadata?> GetFileIndexMetadataAsync(string relativePath, CancellationToken cancellationToken)
    {
        var metadata = await ExecuteWithRetryAsync(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/collections/{_options.CollectionName}/points/scroll")
            {
                Content = JsonContent.Create(new
                {
                    limit = 1,
                    with_payload = true,
                    with_vector = false,
                    filter = new { must = new[] { new { key = "relativePath", match = new { value = relativePath } } } }
                }, options: SerializerOptions)
            };
            AddApiKey(request);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<QdrantScrollResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            var point = payload?.Result?.Points?.FirstOrDefault();
            if (point?.Payload is null)
            {
                return null;
            }

            return new FileIndexMetadata(
                point.Payload.RelativePath,
                point.Payload.CommitSha,
                point.Payload.FileContentHash,
                point.Payload.EmbeddingModel,
                point.Payload.IngestionFingerprint,
                point.Payload.FileChunkCount);
        }, cancellationToken).ConfigureAwait(false);

        if (metadata is not null || !_options.UseInMemoryFallback)
        {
            return metadata;
        }

        return await inMemoryVectorStore.GetFileIndexMetadataAsync(relativePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RetrievedPassage>> SearchAsync(IReadOnlyList<float> queryEmbedding, int limit, SearchFilter? filter, CancellationToken cancellationToken)
    {
        var remoteResults = await ExecuteWithRetryAsync<IReadOnlyList<RetrievedPassage>?>(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/collections/{_options.CollectionName}/points/search")
            {
                Content = JsonContent.Create(
                    new QdrantSearchRequest(queryEmbedding, Math.Max(1, limit), true, BuildQdrantFilter(filter)),
                    options: SerializerOptions)
            };
            AddApiKey(request);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<QdrantSearchResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            if (payload?.Result is not { Count: > 0 })
            {
                return Array.Empty<RetrievedPassage>();
            }

            return payload.Result
                .Where(result => result.Payload is not null)
                .Select(result =>
                {
                    var data = result.Payload!;
                    var chunk = new DocumentChunk(
                        result.Id,
                        data.RepositoryName,
                        data.CommitSha,
                        data.FilePath,
                        data.RelativePath,
                        data.Language,
                        data.Symbol,
                        data.Content,
                        data.ContentHash,
                        data.StartLine,
                        data.EndLine,
                        data.ChunkIndex,
                        queryEmbedding,
                        data.EmbeddingModel,
                        data.IngestionFingerprint,
                        data.FileChunkCount,
                        data.FileContentHash);
                    return new RetrievedPassage(chunk, result.Score);
                })
                .Where(match => InMemoryVectorStore.MatchesFilter(match.Chunk, filter))
                .Take(Math.Max(1, limit))
                .ToArray();
        }, cancellationToken).ConfigureAwait(false);

        // An empty remote result is authoritative. Falling back for an empty result would
        // reintroduce stale points that were deleted remotely; fall back only on transport/API
        // failure (represented by null).
        return remoteResults is not null
            ? remoteResults
            : _options.UseInMemoryFallback
                ? await inMemoryVectorStore.SearchAsync(queryEmbedding, limit, filter, cancellationToken).ConfigureAwait(false)
                : Array.Empty<RetrievedPassage>();
    }

    public async Task<int> DeleteByFilePathAsync(string relativePath, CancellationToken cancellationToken)
    {
        var removedInMemory = await inMemoryVectorStore.DeleteByFilePathAsync(relativePath, cancellationToken).ConfigureAwait(false);

        using (var probe = new HttpRequestMessage(HttpMethod.Get, $"/collections/{_options.CollectionName}"))
        {
            AddApiKey(probe);
            using var probeResponse = await _httpClient.SendAsync(probe, cancellationToken).ConfigureAwait(false);
            if (probeResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return removedInMemory;
            }

            probeResponse.EnsureSuccessStatusCode();
        }

        await ExecuteWithRetryAsync(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/collections/{_options.CollectionName}/points/delete?wait=true")
            {
                Content = JsonContent.Create(new
                {
                    filter = new
                    {
                        must = new object[]
                        {
                            new { key = "relativePath", match = new { value = relativePath } },
                        },
                    },
                }, options: SerializerOptions)
            };
            AddApiKey(request);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }, cancellationToken).ConfigureAwait(false);

        return removedInMemory;
    }

    public async Task<ComponentHealthResult> CheckHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/collections", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new ComponentHealthResult("qdrant", true, $"{_options.BaseUrl} reachable")
                : new ComponentHealthResult("qdrant", false, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return new ComponentHealthResult("qdrant", _options.UseInMemoryFallback, $"{ex.Message} (in-memory fallback {(_options.UseInMemoryFallback ? "active" : "disabled")})");
        }
    }

    private static object? BuildQdrantFilter(SearchFilter? filter)
    {
        if (filter is null)
        {
            return null;
        }

        var must = new List<object>();
        if (filter.Languages is { Count: > 0 } languages)
        {
            must.Add(new
            {
                should = languages.Select(language => new { key = "language", match = new { value = language } }).ToArray(),
            });
        }

        if (filter.PathPrefixes is { Count: > 0 } paths)
        {
            must.Add(new
            {
                should = paths.Select(path => new { key = "relativePath", match = new { text = path } }).ToArray(),
            });
        }

        if (filter.SymbolPrefixes is { Count: > 0 } symbols)
        {
            must.Add(new
            {
                should = symbols.Select(symbol => new { key = "symbol", match = new { text = symbol } }).ToArray(),
            });
        }

        return must.Count == 0 ? null : new { must };
    }

    private async Task EnsureCollectionAsync(int vectorSize, CancellationToken cancellationToken)
    {
        using var probe = new HttpRequestMessage(HttpMethod.Get, $"/collections/{_options.CollectionName}");
        AddApiKey(probe);
        using var probeResponse = await _httpClient.SendAsync(probe, cancellationToken).ConfigureAwait(false);
        if (probeResponse.IsSuccessStatusCode)
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/collections/{_options.CollectionName}")
        {
            Content = JsonContent.Create(new
            {
                vectors = new
                {
                    size = vectorSize,
                    distance = "Cosine"
                }
            })
        };
        AddApiKey(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task ExecuteWithRetryAsync(Func<Task> action, CancellationToken cancellationToken)
        => await ExecuteWithRetryAsync<object?>(async () =>
        {
            await action().ConfigureAwait(false);
            return null;
        }, cancellationToken).ConfigureAwait(false);

    private async Task<T?> ExecuteWithRetryAsync<T>(Func<Task<T?>> action, CancellationToken cancellationToken)
    {
        var attempts = Math.Max(1, _options.RetryCount + 1);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch when (attempt < attempts)
            {
                await Task.Delay(_options.RetryDelayMilliseconds * attempt, cancellationToken).ConfigureAwait(false);
            }
            catch when (_options.UseInMemoryFallback)
            {
                return default;
            }
        }

        return default;
    }

    private void AddApiKey(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Add("api-key", _options.ApiKey);
        }
    }

    private sealed record QdrantPayload(
        string RepositoryName,
        string? CommitSha,
        string FilePath,
        string RelativePath,
        string Language,
        string Symbol,
        string Content,
        string ContentHash,
        int StartLine,
        int EndLine,
        int ChunkIndex,
        string EmbeddingModel,
        string IngestionFingerprint,
        int FileChunkCount,
        string FileContentHash);

    private sealed record QdrantPoint(string Id, IReadOnlyList<float> Vector, QdrantPayload Payload);
    private sealed record QdrantUpsertRequest(IReadOnlyList<QdrantPoint> Points);
    private sealed record QdrantSearchRequest(
        IReadOnlyList<float> Vector,
        int Limit,
        [property: JsonPropertyName("with_payload")] bool WithPayload,
        object? Filter);
    private sealed record QdrantSearchResponse(List<QdrantSearchItem>? Result);
    private sealed record QdrantScrollResponse(QdrantScrollResult? Result);
    private sealed record QdrantScrollResult(List<QdrantScrollItem>? Points);
    private sealed record QdrantScrollItem(string Id, QdrantPayload? Payload);
    private sealed record QdrantSearchItem(string Id, float Score, QdrantPayload? Payload);
}

/// <summary>Reports overall and per-dependency (Qdrant/LM Studio) health.</summary>
public sealed class InfrastructureHealthService(
    IOptionsMonitor<RagOptions> ragOptions,
    IOptionsMonitor<QdrantOptions> qdrantOptions,
    LmStudioClient lmStudioClient,
    QdrantVectorStoreAdapter qdrantAdapter) : IHealthService
{
    public Task<HealthResult> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rag = ragOptions.CurrentValue;
        var qdrant = qdrantOptions.CurrentValue;
        var health = new HealthResult(
            "ok",
            rag.RepositoryPath,
            qdrant.UseInMemoryFallback ? "qdrant-with-memory-fallback" : "qdrant",
            "lmstudio-with-deterministic-fallback");
        return Task.FromResult(health);
    }

    public Task<ComponentHealthResult> CheckQdrantAsync(CancellationToken cancellationToken) => qdrantAdapter.CheckHealthAsync(cancellationToken);

    public Task<ComponentHealthResult> CheckLmStudioAsync(CancellationToken cancellationToken) => lmStudioClient.CheckHealthAsync(cancellationToken);
}
