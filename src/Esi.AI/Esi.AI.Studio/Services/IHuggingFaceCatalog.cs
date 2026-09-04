using Esi.AI.Models;

namespace Esi.AI.Studio.Services;

/// <summary>Reads model metadata and search results from the Hugging Face catalog.</summary>
public interface IHuggingFaceCatalog
{
    Task<IReadOnlyList<HuggingFaceModelInfo>> SearchHuggingFaceAsync(HuggingFaceSearchRequest request, CancellationToken cancellationToken = default);

    Task<HuggingFaceRepositoryMetadata> GetHuggingFaceModelMetadataAsync(string modelId, CancellationToken cancellationToken = default);
}
