using Elsa.Api.Client.Resources.ActivityDescriptors.Enums;
using Elsa.Api.Client.Resources.ActivityDescriptors.Models;
using Elsa.Studio.Workflows.Domain.Contracts;

namespace Esi.AI.Workflow;

internal sealed class LocalActivityRegistryProvider : IActivityRegistryProvider
{
    public Task<IEnumerable<ActivityDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        IEnumerable<ActivityDescriptor> descriptors =
        [
            new()
            {
                TypeName = "Esi.AI.Workflow.Node",
                Namespace = "Esi.AI.Workflow",
                Name = "Node",
                Version = 1,
                Category = "Esi.AI",
                DisplayName = "Esi.AI workflow node",
                Description = "A route node managed by the Esi.AI workflow adapter.",
                IsBrowsable = false,
                IsStart = false,
                IsTerminal = false,
                Ports =
                [
                    new Port { Name = "In", DisplayName = "In", Type = PortType.Flow },
                    new Port { Name = "Done", DisplayName = "Done", Type = PortType.Flow }
                ]
            }
        ];

        return Task.FromResult(descriptors);
    }
}
