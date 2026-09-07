using Esi.AI.Models;
using Esi.AI.Studio.Contracts;

namespace Esi.AI.Studio.Services;

internal sealed class ServerProviderTraceEvents(ProviderTraceStore store) : IProviderTraceEvents
{
    public event Func<ProviderTraceEntry, Task>? ProviderTrace_Create
    {
        add => store.Published += value;
        remove => store.Published -= value;
    }
}