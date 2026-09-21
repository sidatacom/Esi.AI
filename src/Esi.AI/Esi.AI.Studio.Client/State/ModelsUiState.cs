using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

public sealed class ModelsUiState
{
    public bool IsBusy { get; set; }
    public bool IsSearching { get; set; }
    public bool HasSearched { get; set; }
    public string? Message { get; set; }
    public LocalModel? PendingDeleteModel { get; set; }
    public bool DeleteModelFiles { get; set; }
}