namespace Esi.AI.Studio.Services;

/// <summary>Provides the configured directories used by local model discovery and downloads.</summary>
public interface IModelDirectoryCatalog
{
    IReadOnlyList<string> GetModelDirectories();
}
