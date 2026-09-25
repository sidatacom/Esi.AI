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
                DisplayName = "Esi.AI route",
                Description = "Routes a request to an Esi.AI backend.",
                IsBrowsable = true,
                IsStart = false,
                IsTerminal = false,
                Inputs =
                [
                    new InputDescriptor
                    {
                        Name = "Kind",
                        TypeName = "System.String",
                        DisplayName = "Route kind",
                        IsBrowsable = true,
                        UIHint = "singleline"
                    },
                    new InputDescriptor
                    {
                        Name = "RouteTarget",
                        TypeName = "System.String",
                        DisplayName = "Route target",
                        IsBrowsable = true,
                        UIHint = "singleline"
                    }
                ],
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
