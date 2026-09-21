using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

public sealed record ModelsDownloadOptions(string Library, IReadOnlyList<ModelDownloadOption> Options);