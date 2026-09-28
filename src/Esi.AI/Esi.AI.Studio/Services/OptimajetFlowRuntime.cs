using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Esi.AI.Models;
using Microsoft.Extensions.Hosting;
using OptimaJet.Workflow.Core.Builder;
using OptimaJet.Workflow.Core.Model;
using OptimaJet.Workflow.Core.Parser;
using OptimaJet.Workflow.Core.Runtime;
using OptimaJet.Workflow.Migrator;
using OptimaJet.Workflow.SQLite;

namespace Esi.AI.Studio.Services;

/// <summary>Executes published routing definitions through the Optimajet workflow engine.</summary>
public interface IOptimajetFlowRuntime
{
    /// <summary>Executes a backend route as a short-lived workflow process.</summary>
    /// <param name="definition">The published flow definition to execute.</param>
    /// <param name="routeTarget">The configured backend route.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The route target emitted by the workflow action.</returns>
    Task<string> ExecuteRouteAsync(FlowDefinition definition, string routeTarget, CancellationToken cancellationToken = default);
}

/// <summary>Hosts Optimajet with SQLite persistence and executes isolated route processes.</summary>
public sealed class OptimajetFlowRuntime(IConfiguration configuration) : IOptimajetFlowRuntime, IHostedService
{
    private readonly ConcurrentDictionary<string, Lazy<Task>> preparedSchemes = new(StringComparer.Ordinal);
    private readonly RouteActionProvider actionProvider = new();
    private WorkflowRuntime? workflowRuntime;
    private SqliteProvider? persistenceProvider;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        var provider = new SqliteProvider(connectionString);
        var builder = new WorkflowBuilder<XElement>(provider, new XmlWorkflowParser(), provider).WithDefaultCache();
        var runtime = new WorkflowRuntime();

        runtime
            .WithBuilder(builder)
            .WithActionProvider(actionProvider)
            .WithPersistenceProvider(provider)
            .RunMigrations()
            .AsSingleServer()
            .Start();

        persistenceProvider = provider;
        workflowRuntime = runtime;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (workflowRuntime is not null)
            await workflowRuntime.ShutdownAsync(5000).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> ExecuteRouteAsync(
        FlowDefinition definition,
        string routeTarget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.IsPublished)
            throw new InvalidOperationException($"The workflow '{definition.Name}' is not published.");
        if (string.IsNullOrWhiteSpace(routeTarget))
            throw new ArgumentException("A workflow route target is required.", nameof(routeTarget));

        var runtime = workflowRuntime ?? throw new InvalidOperationException("The Optimajet workflow runtime has not started.");
        var provider = persistenceProvider ?? throw new InvalidOperationException("The Optimajet persistence provider has not started.");
        var schemeCode = CreateSchemeCode(definition, routeTarget);
        var saveTask = preparedSchemes.GetOrAdd(
            schemeCode,
            _ => new Lazy<Task>(
                () => provider.SaveSchemeAsync(schemeCode, false, [], CreateSchemeXml(schemeCode, routeTarget), []),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        await saveTask.WaitAsync(cancellationToken).ConfigureAwait(false);

        var processId = Guid.NewGuid();
        try
        {
            await runtime.CreateInstanceAsync(schemeCode, processId, cancellationToken).ConfigureAwait(false);
            return actionProvider.TakeRoute(processId);
        }
        finally
        {
            actionProvider.RemoveRoute(processId);
            if (await runtime.IsProcessExistsAsync(processId).ConfigureAwait(false))
                await runtime.DeleteInstanceAsync(processId).ConfigureAwait(false);
        }
    }

    private static string CreateSchemeCode(FlowDefinition definition, string routeTarget)
    {
        var identity = Encoding.UTF8.GetBytes($"{definition.Id:N}|{definition.Version}|{routeTarget}");
        var digest = Convert.ToHexString(SHA256.HashData(identity))[..20];
        return $"EsiFlow_{digest}";
    }

    private static string CreateSchemeXml(string schemeCode, string routeTarget)
    {
        var process = new XElement("Process",
            new XAttribute("Name", schemeCode),
            new XAttribute("CanBeInlined", "false"),
            new XAttribute("Tags", ""),
            new XAttribute("LogEnabled", "false"),
            new XElement("Activities",
                new XElement("Activity",
                    new XAttribute("Name", "SelectRoute"),
                    new XAttribute("State", "SelectRoute"),
                    new XAttribute("IsInitial", "true"),
                    new XAttribute("IsFinal", "false"),
                    new XAttribute("IsForSetState", "false"),
                    new XAttribute("IsAutoSchemeUpdate", "true"),
                    new XElement("Implementation",
                        new XElement("ActionRef",
                            new XAttribute("Order", "1"),
                            new XAttribute("NameRef", RouteActionProvider.ActionName),
                            new XElement("ActionParameter", routeTarget)))),
                new XElement("Activity",
                    new XAttribute("Name", "Complete"),
                    new XAttribute("State", "Complete"),
                    new XAttribute("IsInitial", "false"),
                    new XAttribute("IsFinal", "true"),
                    new XAttribute("IsForSetState", "false"),
                    new XAttribute("IsAutoSchemeUpdate", "true"))),
            new XElement("Transitions",
                new XElement("Transition",
                    new XAttribute("Name", "SelectRoute_Complete"),
                    new XAttribute("To", "Complete"),
                    new XAttribute("From", "SelectRoute"),
                    new XAttribute("Classifier", "NotSpecified"),
                    new XAttribute("AllowConcatenationType", "And"),
                    new XAttribute("RestrictConcatenationType", "And"),
                    new XAttribute("ConditionsConcatenationType", "And"),
                    new XAttribute("DisableParentStateControl", "false"),
                    new XElement("Triggers", new XElement("Trigger", new XAttribute("Type", "Auto"))),
                    new XElement("Conditions", new XElement("Condition", new XAttribute("Type", "Always"))))));

        return process.ToString(SaveOptions.DisableFormatting);
    }

    private sealed class RouteActionProvider : IWorkflowActionProvider
    {
        public const string ActionName = "EsiAiSelectBackend";
        private readonly ConcurrentDictionary<Guid, string> routeTargets = new();

        public void ExecuteAction(string name, ProcessInstance processInstance, WorkflowRuntime runtime, string actionParameter)
        {
            if (!string.Equals(name, ActionName, StringComparison.Ordinal))
                throw new InvalidOperationException($"Workflow action '{name}' is not supported.");

            routeTargets[processInstance.ProcessId] = actionParameter;
        }

        public Task ExecuteActionAsync(
            string name,
            ProcessInstance processInstance,
            WorkflowRuntime runtime,
            string actionParameter,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ExecuteAction(name, processInstance, runtime, actionParameter);
            return Task.CompletedTask;
        }

        public bool ExecuteCondition(string name, ProcessInstance processInstance, WorkflowRuntime runtime, string actionParameter) =>
            throw new InvalidOperationException($"Workflow condition '{name}' is not supported.");

        public Task<bool> ExecuteConditionAsync(
            string name,
            ProcessInstance processInstance,
            WorkflowRuntime runtime,
            string actionParameter,
            CancellationToken token) =>
            Task.FromException<bool>(new InvalidOperationException($"Workflow condition '{name}' is not supported."));

        public bool IsActionAsync(string name, string schemeCode) => false;

        public bool IsConditionAsync(string name, string schemeCode) => false;

        public List<string> GetActions(string schemeCode, NamesSearchType namesSearchType) => [ActionName];

        public List<string> GetConditions(string schemeCode, NamesSearchType namesSearchType) => [];

        public string TakeRoute(Guid processId) => routeTargets.TryRemove(processId, out var routeTarget)
            ? routeTarget
            : throw new InvalidOperationException("The workflow completed without selecting a backend route.");

        public void RemoveRoute(Guid processId) => routeTargets.TryRemove(processId, out _);
    }
}