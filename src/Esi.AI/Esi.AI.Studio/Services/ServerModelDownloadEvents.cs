using Esi.AI.Models;
using Esi.AI.Studio.Client.Services;

namespace Esi.AI.Studio.Services;

internal sealed class ServerModelDownloadEvents : IModelDownloadEvents, IModelRuntimeEvents
{
    public event Func<ModelDownloadUpdate, Task>? ModelDownloadUpdated
    {
        add { }
        remove { }
    }

    public event Func<Task>? ModelRuntimeStatusUpdated
    {
        add { }
        remove { }
    }
}