using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

internal sealed record OpenVinoAdvancedState(
    string Device,
    IReadOnlyDictionary<string, OpenVinoDeviceSetting> Devices,
    string CacheDirectory,
    int MaxNewTokens,
    float Temperature,
    float TopP,
    bool DoSample,
    int TopK,
    float RepetitionPenalty,
    OpenVinoNpuSettings? Npu);