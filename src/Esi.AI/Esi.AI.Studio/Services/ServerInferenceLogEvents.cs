using Esi.AI.Models;
using Esi.AI.Studio.Contracts;

namespace Esi.AI.Studio.Services;

internal sealed class ServerInferenceLogEvents(InferenceLogService service) : IInferenceLogEvents
{
    public event Func<IReadOnlyList<InferenceLog>, Task>? InferenceLog_Read
    {
        add => service.Read += value;
        remove => service.Read -= value;
    }

    public event Func<InferenceLog, Task>? InferenceLog_Create
    {
        add => service.Created += value;
        remove => service.Created -= value;
    }

    public event Func<InferenceLog, Task>? InferenceLog_Update
    {
        add => service.Updated += value;
        remove => service.Updated -= value;
    }

    public event Func<InferenceLog, Task>? InferenceLog_Delete
    {
        add => service.Deleted += value;
        remove => service.Deleted -= value;
    }
}
