using System.Text.Json;
using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

internal static class PrerenderStateSnapshots
{
    private const int MaximumPayloadBytes = 128 * 1024;

    /// <summary>Captures a state value only when its serialized payload is within the page budget.</summary>
    public static T? TryCapture<T>(T value) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.SerializeToUtf8Bytes(value).Length <= MaximumPayloadBytes ? value : null;
    }

    /// <summary>Captures loaded-model state without diagnostic logs that pages do not display.</summary>
    public static ModelLoadStatus? TryCaptureModelLoadStatus(ModelLoadStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var snapshot = status with
        {
            LoadLog = string.Empty,
            LoadedModels = status.LoadedModels.Select(model => model with { LoadLog = string.Empty }).ToArray()
        };
        return TryCapture(snapshot);
    }
}