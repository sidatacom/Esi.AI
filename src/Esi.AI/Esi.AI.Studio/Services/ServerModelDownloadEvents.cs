using Esi.AI.Models;
using Esi.AI.Studio.Contracts;

namespace Esi.AI.Studio.Services;

internal sealed class ServerModelDownloadEvents : IModelDownloadEvents, IModelRuntimeEvents, IBackendRequirementEvents, IBackendRuntimeEvents
{
    public event Func<ModelDownloadUpdate, Task>? ModelDownload_Create
    {
        add { }
        remove { }
    }

    public event Func<ModelDownloadUpdate, Task>? ModelDownload_Update
    {
        add { }
        remove { }
    }

    public event Func<ModelDownloadUpdate, Task>? ModelDownload_Delete
    {
        add { }
        remove { }
    }

    public event Func<ModelLoadStatus, Task>? LoadedModel_Create
    {
        add { }
        remove { }
    }

    public event Func<ModelLoadStatus, Task>? LoadedModel_Update
    {
        add { }
        remove { }
    }

    public event Func<ModelLoadStatus, Task>? LoadedModel_Delete
    {
        add { }
        remove { }
    }

    public event Func<BackendRequirementState, Task>? BackendRequirementStateUpdated
    {
        add { }
        remove { }
    }

    public event Func<BackendRuntimeStatus, Task>? BackendRuntime_Create
    {
        add { }
        remove { }
    }

    public event Func<BackendRuntimeStatus, Task>? BackendRuntime_Update
    {
        add { }
        remove { }
    }

    public event Func<BackendRuntimeStatus, Task>? BackendRuntime_Delete
    {
        add { }
        remove { }
    }
}