using Esi.AI.Models;
using Esi.AI.Studio.Client.Services;

namespace Esi.AI.Studio.Services;

internal sealed class ServerModelDownloadEvents : IModelDownloadEvents
{
    public event Func<ModelDownloadUpdate, Task>? ModelDownloadUpdated
    {
        add { }
        remove { }
    }
}