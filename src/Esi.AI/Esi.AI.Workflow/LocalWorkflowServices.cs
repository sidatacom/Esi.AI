using Elsa.Api.Client.Resources.WorkflowDefinitions.Models;
using Elsa.Api.Client.Resources.WorkflowDefinitions.Requests;
using Elsa.Api.Client.Resources.WorkflowDefinitions.Responses;
using Elsa.Api.Client.Resources.Features.Models;
using Elsa.Api.Client.Resources.CommitStrategies.Models;
using Elsa.Api.Client.Resources.IncidentStrategies.Models;
using Elsa.Api.Client.Resources.LogPersistenceStrategies;
using Elsa.Api.Client.Resources.StorageDrivers.Models;
using Elsa.Api.Client.Resources.Scripting.Models;
using Elsa.Api.Client.Resources.VariableTypes.Models;
using Elsa.Api.Client.Resources.WorkflowActivationStrategies.Models;
using Elsa.Api.Client.Shared.Models;
using Elsa.Studio.Models;
using Elsa.Studio.Contracts;
using Elsa.Studio.Authentication.Abstractions.Contracts;
using Microsoft.AspNetCore.Http.Connections.Client;
using Elsa.Studio.Workflows.Domain.Contracts;
using Elsa.Studio.Workflows.Domain.Models;

namespace Esi.AI.Workflow;

internal sealed class LocalWorkflowDefinitionService : IWorkflowDefinitionService
{
    public Task<PagedListResponse<WorkflowDefinitionSummary>> ListAsync(ListWorkflowDefinitionsRequest request, VersionOptions? versionOptions = null, CancellationToken cancellationToken = default) => throw Unsupported();
    public Task<WorkflowDefinition?> FindByDefinitionIdAsync(string definitionId, VersionOptions? versionOptions = null, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowDefinition?>(null);
    public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowDefinition?>(null);
    public Task<IEnumerable<WorkflowDefinition>> FindManyByIdAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<WorkflowDefinition>>([]);
    public Task<ActivityNode?> FindSubgraphAsync(string id, string? parentNodeId = null, CancellationToken cancellationToken = default) => throw Unsupported();
    public Task<GetPathSegmentsResponse?> GetPathSegmentsAsync(string id, string? childNodeId = null, CancellationToken cancellationToken = default) => throw Unsupported();
    public Task<bool> DeleteAsync(string definitionId, CancellationToken cancellationToken = default) => throw Unsupported();
    public Task<bool> DeleteVersionAsync(WorkflowDefinitionVersion workflowDefinitionVersion, CancellationToken cancellationToken = default) => throw Unsupported();
    public Task<SaveWorkflowDefinitionResponse> PublishAsync(string definitionId, CancellationToken cancellationToken = default) => throw Unsupported();
    public Task<Result<WorkflowDefinition, ValidationErrors>> RetractAsync(string definitionId, CancellationToken cancellationToken = default) => throw Unsupported();
    public Task<long> BulkDeleteAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => throw Unsupported();
    public Task<long> BulkDeleteVersionsAsync(IEnumerable<WorkflowDefinitionVersion> workflowDefinitionVersions, CancellationToken cancellationToken = default) => throw Unsupported();
    public Task<BulkPublishWorkflowDefinitionsResponse> BulkPublishAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => throw Unsupported();
    public Task<BulkRetractWorkflowDefinitionsResponse> BulkRetractAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => throw Unsupported();
    public Task<bool> GetIsNameUniqueAsync(string name, string? definitionId = null, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<string> GenerateUniqueNameAsync(CancellationToken cancellationToken = default) => Task.FromResult("Esi.AI workflow");
    public Task<Result<WorkflowDefinition, ValidationErrors>> CreateNewDefinitionAsync(string name, string? description = null, Action<SaveWorkflowDefinitionRequest>? configureRequest = null, CancellationToken cancellationToken = default) => throw Unsupported();
    public Task<Result<WorkflowDefinition, ValidationErrors>> CreateNewDefinitionAsync(string name, string? description, string? rootActivityTemplateKey, Action<SaveWorkflowDefinitionRequest>? configureRequest = null, CancellationToken cancellationToken = default) => throw Unsupported();
    public Task<FileDownload> ExportDefinitionAsync(string definitionId, VersionOptions? versionOptions = null, bool includeConsumingWorkflows = false, CancellationToken cancellationToken = default) => throw Unsupported();
    public Task<FileDownload> BulkExportDefinitionsAsync(IEnumerable<string> ids, bool includeConsumingWorkflows = false, CancellationToken cancellationToken = default) => throw Unsupported();
    public Task<UpdateConsumingWorkflowReferencesResponse> UpdateReferencesAsync(string definitionId, CancellationToken cancellationToken = default) => throw Unsupported();
    public Task<ExecuteWorkflowResult> ExecuteAsync(string definitionId, ExecuteWorkflowDefinitionRequest? request, CancellationToken cancellationToken = default) => throw Unsupported();

    private static NotSupportedException Unsupported() => new("This Elsa Studio operation is owned by Esi.AI and is exposed through the Esi.AI workflow adapter.");
}

internal sealed class LocalWorkflowDefinitionEditorService : IWorkflowDefinitionEditorService
{
    public async Task<Result<SaveWorkflowDefinitionResponse, ValidationErrors>> SaveAsync(WorkflowDefinition workflowDefinition, bool publish, Func<WorkflowDefinition, Task>? workflowSavedCallback = null, CancellationToken cancellationToken = default)
    {
        workflowDefinition.IsPublished = publish;
        if (workflowSavedCallback is not null)
            await workflowSavedCallback(workflowDefinition);
        return new Result<SaveWorkflowDefinitionResponse, ValidationErrors>(new SaveWorkflowDefinitionResponse(workflowDefinition, false, 0));
    }

    public async Task<SaveWorkflowDefinitionResponse> PublishAsync(WorkflowDefinition workflowDefinition, Func<WorkflowDefinition, Task>? workflowPublishedCallback = null, CancellationToken cancellationToken = default)
    {
        workflowDefinition.IsPublished = true;
        if (workflowPublishedCallback is not null)
            await workflowPublishedCallback(workflowDefinition);
        return new SaveWorkflowDefinitionResponse(workflowDefinition, false, 0);
    }

    public Task<Result<WorkflowDefinition, ValidationErrors>> RetractAsync(WorkflowDefinition workflowDefinition, Func<WorkflowDefinition, Task>? workflowRetractedCallback = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<FileDownload> ExportAsync(WorkflowDefinition workflowDefinition, bool includeConsumingWorkflows = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class LocalBackendApiClientProvider : IBackendApiClientProvider
{
    public Uri Url => new("http://local.esi.ai");

    public ValueTask<T> GetApiAsync<T>(CancellationToken cancellationToken = default) where T : class =>
        throw new NotSupportedException($"Elsa backend API calls are not part of the local Esi.AI workflow editor: {typeof(T).FullName}.");
}

    internal sealed class LocalRemoteFeatureProvider : IRemoteFeatureProvider
    {
        public Task<bool> IsEnabledAsync(string featureName, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<IEnumerable<FeatureDescriptor>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<FeatureDescriptor>>([]);
    }

    internal sealed class LocalStorageDriverService : IStorageDriverService
    {
        public Task<IEnumerable<StorageDriverDescriptor>> GetStorageDriversAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<StorageDriverDescriptor>>([]);
    }

    internal sealed class LocalVariableTypeService : IVariableTypeService
    {
        public Task<IEnumerable<VariableTypeDescriptor>> GetVariableTypesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<VariableTypeDescriptor>>(
            [
                new("System.Boolean", "Boolean", "Primitives", null),
                new("System.Int32", "Integer", "Primitives", null),
                new("System.Int64", "Long", "Primitives", null),
                new("System.Decimal", "Decimal", "Primitives", null),
                new("System.Double", "Double", "Primitives", null),
                new("System.String", "String", "Text", null),
                new("System.DateTime", "DateTime", "Primitives", null),
                new("System.Guid", "Guid", "Primitives", null)
            ]);
    }

    internal sealed class LocalWorkflowActivationStrategyService : IWorkflowActivationStrategyService
    {
        public Task<IEnumerable<WorkflowActivationStrategyDescriptor>> GetWorkflowActivationStrategiesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<WorkflowActivationStrategyDescriptor>>([]);
    }

    internal sealed class LocalIncidentStrategiesProvider : IIncidentStrategiesProvider
    {
        public ValueTask<IEnumerable<IncidentStrategyDescriptor>> GetIncidentStrategiesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IEnumerable<IncidentStrategyDescriptor>>([]);
    }

    internal sealed class LocalLogPersistenceStrategyService : ILogPersistenceStrategyService
    {
        public Task<IEnumerable<LogPersistenceStrategyDescriptor>> GetLogPersistenceStrategiesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<LogPersistenceStrategyDescriptor>>([]);
    }

    internal sealed class LocalCommitStrategiesProvider : ICommitStrategiesProvider
    {
        public ValueTask<IEnumerable<CommitStrategyDescriptor>> GetWorkflowCommitStrategiesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IEnumerable<CommitStrategyDescriptor>>([]);

        public ValueTask<IEnumerable<CommitStrategyDescriptor>> GetActivityCommitStrategiesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IEnumerable<CommitStrategyDescriptor>>([]);
    }

internal sealed class LocalHttpConnectionOptionsConfigurator : IHttpConnectionOptionsConfigurator
{
    public Task ConfigureAsync(HttpConnectionOptions options, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class LocalExpressionService : IExpressionService
{
    public Task<IEnumerable<ExpressionDescriptor>> ListDescriptorsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<ExpressionDescriptor>>([]);

    public Task<ExpressionDescriptor?> GetByTypeAsync(string type, CancellationToken cancellationToken = default) =>
        Task.FromResult<ExpressionDescriptor?>(null);
}

internal sealed class LocalFeatureService(IEnumerable<IFeature> features) : IFeatureService
{
    public event Action? Initialized;

    public IEnumerable<IFeature> GetFeatures() => features;

    public async Task InitializeFeaturesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var feature in features)
            await feature.InitializeAsync(cancellationToken);

        Initialized?.Invoke();
    }
}
