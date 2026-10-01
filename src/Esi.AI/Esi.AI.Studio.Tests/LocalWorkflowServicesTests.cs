using Esi.AI.Workflow;
using Elsa.Studio.Contracts;
using Elsa.Studio.Services;
using Elsa.Studio.Workflows.Domain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class LocalWorkflowServicesTests
{
    [TestMethod]
    public async Task AddEsiAiWorkflowDesigner_NoRemoteBackend_DisablesRemoteFeatures()
    {
        var services = new ServiceCollection();
        services.AddEsiAiWorkflowDesigner();
        using var serviceProvider = services.BuildServiceProvider();
        var featureProvider = serviceProvider.GetRequiredService<IRemoteFeatureProvider>();

        Assert.IsFalse(await featureProvider.IsEnabledAsync("Elsa.Resilience"));
        Assert.IsEmpty(await featureProvider.ListAsync());
    }

    [TestMethod]
    public void AddEsiAiWorkflowDesigner_RegistersTypeDefinitionService()
    {
        var services = new ServiceCollection();
        services.AddEsiAiWorkflowDesigner();
        using var serviceProvider = services.BuildServiceProvider();

        Assert.IsNotNull(serviceProvider.GetRequiredService<TypeDefinitionService>());
    }

    [TestMethod]
    public async Task AddEsiAiWorkflowDesigner_ProvidesExpressionDescriptorsForExpressionEditor()
    {
        var services = new ServiceCollection();
        services.AddEsiAiWorkflowDesigner();
        using var serviceProvider = services.BuildServiceProvider();
        var expressionService = serviceProvider.GetRequiredService<IExpressionService>();

        var descriptors = (await expressionService.ListDescriptorsAsync()).ToArray();
        var expressionEditorDescriptors = descriptors
            .Where(descriptor => descriptor.Type is not ("Literal" or "Object" or "Variable" or "Input"))
            .ToArray();

        Assert.IsNotEmpty(expressionEditorDescriptors);
        Assert.AreEqual("JavaScript", expressionEditorDescriptors[0].Type);
        Assert.AreEqual("javascript", expressionEditorDescriptors[0].Properties["MonacoLanguage"]);
        Assert.AreEqual("Literal", (await expressionService.GetByTypeAsync("Literal"))?.Type);
        Assert.AreEqual("JavaScript", (await expressionService.GetByTypeAsync("JavaScript"))?.Type);
        Assert.IsNull(await expressionService.GetByTypeAsync("Unknown"));
    }

    [TestMethod]
    public async Task AddEsiAiWorkflowDesigner_WorkflowPropertyMetadata_UsesLocalValues()
    {
        var services = new ServiceCollection();
        services.AddEsiAiWorkflowDesigner();
        using var serviceProvider = services.BuildServiceProvider();
        var storageDriverService = serviceProvider.GetRequiredService<IStorageDriverService>();
        var variableTypeService = serviceProvider.GetRequiredService<IVariableTypeService>();
        var activationStrategyService = serviceProvider.GetRequiredService<IWorkflowActivationStrategyService>();
        var incidentStrategiesProvider = serviceProvider.GetRequiredService<IIncidentStrategiesProvider>();
        var logPersistenceStrategyService = serviceProvider.GetRequiredService<ILogPersistenceStrategyService>();
        var commitStrategiesProvider = serviceProvider.GetRequiredService<ICommitStrategiesProvider>();

        Assert.IsEmpty(await storageDriverService.GetStorageDriversAsync());
        CollectionAssert.AreEqual(
            new[] { "System.Boolean", "System.Int32", "System.Int64", "System.Decimal", "System.Double", "System.String", "System.DateTime", "System.Guid" },
            (await variableTypeService.GetVariableTypesAsync()).Select(variableType => variableType.TypeName).ToArray());
        Assert.IsEmpty(await activationStrategyService.GetWorkflowActivationStrategiesAsync());
        Assert.IsEmpty(await incidentStrategiesProvider.GetIncidentStrategiesAsync());
        Assert.IsEmpty(await logPersistenceStrategyService.GetLogPersistenceStrategiesAsync());
        Assert.IsEmpty(await commitStrategiesProvider.GetWorkflowCommitStrategiesAsync());
        Assert.IsEmpty(await commitStrategiesProvider.GetActivityCommitStrategiesAsync());
    }
}