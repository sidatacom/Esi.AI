using Elsa.Studio.DomInterop.Extensions;
using Elsa.Studio.Contracts;
using Elsa.Studio.Authentication.Abstractions.Contracts;
using Elsa.Studio.Extensions;
using Elsa.Studio.Services;
using Elsa.Studio.Workflows.Designer.Extensions;
using Elsa.Studio.Workflows.Designer.Interop;
using Elsa.Studio.Workflows.Domain.Contracts;
using Elsa.Studio.Workflows.Domain.Providers;
using Elsa.Studio.Workflows.Domain.Services;
using Elsa.Studio.Workflows.Extensions;
using Elsa.Studio.Workflows.UI.Contracts;
using Elsa.Studio.Workflows.UI.Providers;
using Elsa.Studio.Workflows.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor;
using MudBlazor.Services;

namespace Esi.AI.Workflow;

/// <summary>Registers the Open Source Elsa-based Esi.AI workflow designer.</summary>
public static class EsiAiWorkflowServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Elsa designer services required by <see cref="EsiAiWorkflowDesigner"/>.
    /// </summary>
    public static IServiceCollection AddEsiAiWorkflowDesigner(this IServiceCollection services)
    {
        return services
            .AddCoreInternal()
            .AddSharedServices()
            .AddWorkflowsModule()
            .AddWorkflowsDesigner()
            .AddDomInterop()
            .AddClipboardInterop()
            .AddScoped<DesignerJsInterop>()
            .RemoveAll<IWorkflowDefinitionService>()
            .RemoveAll<IWorkflowDefinitionEditorService>()
            .RemoveAll<IActivityRegistryProvider>()
            .RemoveAll<IActivityRegistry>()
            .RemoveAll<IStorageDriverService>()
            .RemoveAll<IVariableTypeService>()
            .RemoveAll<IWorkflowActivationStrategyService>()
            .RemoveAll<IIncidentStrategiesProvider>()
            .RemoveAll<ILogPersistenceStrategyService>()
            .RemoveAll<ICommitStrategiesProvider>()
            .AddScoped<IWorkflowDefinitionService, LocalWorkflowDefinitionService>()
            .AddScoped<IWorkflowDefinitionEditorService, LocalWorkflowDefinitionEditorService>()
            .RemoveAll<IRemoteFeatureProvider>()
            .AddScoped<IRemoteFeatureProvider, LocalRemoteFeatureProvider>()
            .AddScoped<IActivityRegistryProvider, LocalActivityRegistryProvider>()
            .AddScoped<IActivityRegistry, DefaultActivityRegistry>()
            .AddScoped<IStorageDriverService, LocalStorageDriverService>()
            .AddScoped<IVariableTypeService, LocalVariableTypeService>()
            .AddScoped<IWorkflowActivationStrategyService, LocalWorkflowActivationStrategyService>()
            .AddScoped<IIncidentStrategiesProvider, LocalIncidentStrategiesProvider>()
            .AddScoped<ILogPersistenceStrategyService, LocalLogPersistenceStrategyService>()
            .AddScoped<ICommitStrategiesProvider, LocalCommitStrategiesProvider>()
            .AddScoped<IBackendApiClientProvider, LocalBackendApiClientProvider>()
            .AddScoped<IHttpConnectionOptionsConfigurator, LocalHttpConnectionOptionsConfigurator>()
            .AddScoped<IIdentityGenerator, RandomLongIdentityGenerator>()
            .AddScoped<IActivityNameGenerator, DefaultActivityNameGenerator>()
            .AddScoped<IActivityPortService, DefaultActivityPortService>()
            .AddScoped<IActivityPortProvider, DefaultActivityPortProvider>()
            .AddScoped<IActivityDisplaySettingsRegistry, DefaultActivityDisplaySettingsRegistry>()
            .AddScoped<IActivityDisplaySettingsProvider, DefaultActivityDisplaySettingsProvider>()
            .RemoveAll<IFeatureService>()
            .RemoveAll<IExpressionService>()
            .RemoveAll<TypeDefinitionService>()
            .RemoveAll<IMonacoHandler>()
            .AddScoped<IExpressionService, LocalExpressionService>()
            .AddScoped<IFeatureService, LocalFeatureService>()
            .AddMudServices();
    }
}
