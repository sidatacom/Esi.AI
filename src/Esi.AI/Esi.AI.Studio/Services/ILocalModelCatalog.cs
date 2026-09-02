namespace Esi.AI.Studio.Services;

public interface ILocalModelCatalog
{
    Task<IReadOnlyList<LocalModelInfo>> ScanLocalModelsAsync(CancellationToken cancellationToken = default);
}