using System.Net.Http;
using Esi.AI.Models;
using Esi.AI.Studio.Client.Services;
using Esi.AI.Studio.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Components;

namespace Esi.AI.Studio.Client.Pages;

public partial class Benchmark : IAsyncDisposable
{
    private const int MaxRepeatCount = 127850;
    private const string RunIdPrefix = "QWEN38-BENCH-V1-";

    private readonly Qwen38BenchmarkRunner benchmarkRunner;
    private IReadOnlyList<ModelConfiguration> configurations = [];
    private ModelLoadStatus? loadStatus;
    private readonly List<BenchmarkMeasurement> results = [];
    private readonly List<BenchmarkRun> history = [];
    private ConfigurationBackend? selectedBackend;
    private Guid? selectedProfileId;
    private Guid? historyProfileId;
    private string? historyModelPath;
    private Guid? shortBenchmarkProfileId;
    private Qwen38BenchmarkSummary? shortSummary;
    private CancellationTokenSource? runCancellation;
    private int shortRepeatCount = 390;
    private int longRepeatCount = MaxRepeatCount;
    private int warmups = 1;
    private int measuredRuns = 5;
    private int currentRun;
    private int totalRuns;
    private bool isLoading = true;
    private bool isRunning;
    private string activeRunKind = string.Empty;
    private string? errorMessage;
    private string? statusMessage;

    public Benchmark(HttpClient httpClient)
    {
        benchmarkRunner = new Qwen38BenchmarkRunner(httpClient);
    }

    private IReadOnlyList<ModelConfiguration> BenchmarkProfiles => configurations
        .Where(configuration =>
            configuration.Backend is ConfigurationBackend.Llama or ConfigurationBackend.OpenVino or ConfigurationBackend.Vllm &&
            configuration.Name.StartsWith("Benchmark", StringComparison.OrdinalIgnoreCase))
        .OrderBy(configuration => configuration.BackendVariantId, StringComparer.OrdinalIgnoreCase)
        .ThenBy(configuration => configuration.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private IReadOnlyList<ConfigurationBackend> AvailableBackends => BenchmarkProfiles
        .Select(profile => profile.Backend)
        .Distinct()
        .Order()
        .ToArray();

    private IReadOnlyList<ModelConfiguration> SelectedBackendProfiles => selectedBackend is ConfigurationBackend backend
        ? BenchmarkProfiles.Where(profile => profile.Backend == backend).ToArray()
        : [];

    private ModelConfiguration? SelectedProfile => selectedProfileId is Guid id
        ? SelectedBackendProfiles.FirstOrDefault(profile => profile.Id == id)
        : null;

    private bool IsSelectedProfileLoaded => SelectedProfile is { } profile && IsProfileLoaded(profile);

    private IReadOnlyList<string> HistoryModelPaths => history
        .Select(run => run.ProfileSnapshot.ModelPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private IReadOnlyList<(Guid ProfileId, string ProfileName)> HistoryProfiles => history
        .Where(run => string.IsNullOrWhiteSpace(historyModelPath) ||
            string.Equals(run.ProfileSnapshot.ModelPath, historyModelPath, StringComparison.OrdinalIgnoreCase))
        .GroupBy(run => run.ProfileId)
        .Select(group =>
        {
            var run = group.First();
            return (run.ProfileId, $"{run.ProfileSnapshot.Name} · {run.ProfileSnapshot.BackendVariantId}");
        })
        .OrderBy(profile => profile.Item2, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private IReadOnlyList<BenchmarkRun> FilteredHistory => history
        .Where(run => string.IsNullOrWhiteSpace(historyModelPath) ||
            string.Equals(run.ProfileSnapshot.ModelPath, historyModelPath, StringComparison.OrdinalIgnoreCase))
        .Where(run => historyProfileId is null || run.ProfileId == historyProfileId)
        .OrderByDescending(run => run.CreatedAtUtc)
        .ToArray();

    private bool IsProfileLoaded(ModelConfiguration profile) =>
        loadStatus?.LoadedModels.Any(model =>
            model.IsModelLoaded &&
            !model.IsLoading &&
            string.Equals(model.BackendVariantId, profile.BackendVariantId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetFullPath(model.ModelPath), Path.GetFullPath(profile.ModelPath), StringComparison.OrdinalIgnoreCase)) == true;

    private bool CanStartBenchmark => !isLoading &&
        !isRunning &&
        SelectedProfile is not null &&
        IsSelectedProfileLoaded &&
        shortRepeatCount is >= 1 and <= MaxRepeatCount &&
        warmups is >= 0 and <= 20 &&
        measuredRuns is >= 1 and <= 20;

    private bool CanStartLongBenchmark => CanStartBenchmark &&
        shortBenchmarkProfileId == selectedProfileId &&
        shortBenchmarkPassed &&
        longRepeatCount is >= 1 and <= MaxRepeatCount;

    private bool shortBenchmarkPassed;

    protected override async Task OnInitializedAsync()
    {
        RuntimeEvents.LoadedModel_Create += HandleLoadedModelChangedAsync;
        RuntimeEvents.LoadedModel_Update += HandleLoadedModelChangedAsync;
        RuntimeEvents.LoadedModel_Delete += HandleLoadedModelChangedAsync;
        BenchmarkEvents.BenchmarkRun_Read += HandleBenchmarkRunReadAsync;
        BenchmarkEvents.BenchmarkRun_Create += HandleBenchmarkRunChangedAsync;
        BenchmarkEvents.BenchmarkRun_Update += HandleBenchmarkRunChangedAsync;
        BenchmarkEvents.BenchmarkRun_Delete += HandleBenchmarkRunDeletedAsync;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        isLoading = true;
        errorMessage = null;
        statusMessage = null;
        try
        {
            var configurationsTask = DataService.ModelConfiguration_ReadAsync();
            var loadStatusTask = DataService.LoadedModel_ReadAsync();
            var benchmarkRunsTask = DataService.BenchmarkRun_ReadAsync();
            await Task.WhenAll(configurationsTask, loadStatusTask, benchmarkRunsTask);
            configurations = await configurationsTask;
            loadStatus = await loadStatusTask;
            history.Clear();
            history.AddRange(await benchmarkRunsTask);
            if (selectedBackend is ConfigurationBackend backend && !AvailableBackends.Contains(backend))
                selectedBackend = null;
            if (SelectedProfile is null)
                selectedProfileId = null;
            if (historyProfileId is Guid historyId && !history.Any(run => run.ProfileId == historyId))
                historyProfileId = null;
            if (!HistoryModelPaths.Contains(historyModelPath, StringComparer.OrdinalIgnoreCase))
                historyModelPath = null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or HubException or OperationCanceledException or HttpRequestException)
        {
            Logger.LogError(exception, "Could not load benchmark profiles and runtime status.");
            errorMessage = $"Benchmark-Daten konnten nicht geladen werden: {exception.Message}";
        }
        finally
        {
            isLoading = false;
        }
    }

    private void SelectBackend(ChangeEventArgs args)
    {
        selectedBackend = Enum.TryParse<ConfigurationBackend>(args.Value?.ToString(), out var backend) &&
            AvailableBackends.Contains(backend)
            ? backend
            : null;
        selectedProfileId = null;
        ResetCurrentResults();
    }

    private void SelectProfile(ChangeEventArgs args)
    {
        selectedProfileId = Guid.TryParse(args.Value?.ToString(), out var id) &&
            SelectedBackendProfiles.Any(profile => profile.Id == id)
            ? id
            : null;
        ResetCurrentResults();
    }

    private void ResetCurrentResults()
    {
        shortBenchmarkProfileId = null;
        shortBenchmarkPassed = false;
        shortSummary = null;
        results.Clear();
        errorMessage = null;
        statusMessage = null;
        StateHasChanged();
    }

    private async Task RunShortBenchmarkAsync()
    {
        if (!CanStartBenchmark || SelectedProfile is not { } profile)
            return;

        shortBenchmarkProfileId = null;
        shortBenchmarkPassed = false;
        shortSummary = null;
        results.Clear();
        var completed = await RunSeriesAsync(profile, shortRepeatCount, warmups, measuredRuns, "short");

        var measured = results.Where(result => result.Kind == "measured").ToArray();
        shortSummary = CreateSummary(measured);
        if (!completed)
            return;

        shortBenchmarkPassed = measured.Length == measuredRuns &&
            measured.All(result => result.CompletionLengthMatchesLimit && result.Fits131072Context);
        if (shortBenchmarkPassed)
        {
            shortBenchmarkProfileId = profile.Id;
            statusMessage = "Kurzbenchmark vollständig. Die 128k-Kontextprobe ist jetzt freigeschaltet.";
        }
        else
        {
            statusMessage = "Kurzbenchmark beendet, aber mindestens ein Messlauf hat die Vergleichskriterien nicht erfüllt.";
        }
    }

    private async Task RunLongBenchmarkAsync()
    {
        if (!CanStartLongBenchmark || SelectedProfile is not { } profile)
            return;

        results.Clear();
        if (await RunSeriesAsync(profile, longRepeatCount, 0, 1, "long"))
            statusMessage = "128k-Kontextprobe abgeschlossen. Vergleiche Prompt-Tokens und 131072-Token-Fit mit dem Kurzbenchmark.";
    }

    private async Task<bool> RunSeriesAsync(
        ModelConfiguration profile,
        int repeatCount,
        int warmupCount,
        int runCount,
        string seriesKind)
    {
        errorMessage = null;
        statusMessage = null;
        isRunning = true;
        totalRuns = warmupCount + runCount;
        currentRun = 0;
        activeRunKind = warmupCount > 0 ? "warmup" : seriesKind;
        runCancellation = new CancellationTokenSource();
        var cancellationToken = runCancellation.Token;
        var settings = Qwen38BenchmarkRunner.CreateSettings(seriesKind, repeatCount, warmupCount, runCount);
        BenchmarkRun? savedRun = null;
        var seriesCompleted = false;
        var finalState = BenchmarkRunState.Failed;
        string? failureMessage = null;
        try
        {
            var currentProfile = await DataService.ModelConfiguration_ReadAsync(profile.Id, cancellationToken);
            if (currentProfile is null)
                throw new InvalidOperationException("Das ausgewählte Profil wurde inzwischen gelöscht.");
            if (!IsProfileLoaded(currentProfile))
                throw new InvalidOperationException("Das ausgewählte Profil ist nicht mehr geladen. Lade es erneut, bevor du den Benchmark startest.");

            savedRun = await DataService.BenchmarkRun_CreateAsync(
                new BenchmarkRun(
                    Guid.Empty,
                    currentProfile.Id,
                    currentProfile,
                    settings,
                    [],
                    BenchmarkRunState.Running,
                    DateTime.UtcNow,
                    null,
                    null),
                cancellationToken);

            for (var index = 0; index < totalRuns; index++)
            {
                activeRunKind = index < warmupCount ? "warmup" : seriesKind == "long" ? "long" : "measured";
                currentRun = index + 1;
                var runId = $"{RunIdPrefix}{currentRun.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)}";
                var result = await benchmarkRunner.RunAsync(
                    currentProfile,
                    settings,
                    runId,
                    activeRunKind,
                    cancellationToken);
                results.Add(result);
                savedRun = await DataService.BenchmarkRun_UpdateAsync(
                    savedRun with { Measurements = savedRun.Measurements.Append(result).ToArray() },
                    CancellationToken.None);
                await InvokeAsync(StateHasChanged);
            }
            seriesCompleted = true;
            finalState = BenchmarkRunState.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            statusMessage = "Benchmark-Lauf abgebrochen.";
            finalState = BenchmarkRunState.Cancelled;
        }
        catch (Exception exception) when (exception is
            HttpRequestException or
            InvalidDataException or
            IOException or
            System.Text.Json.JsonException or
            OperationCanceledException or
            HubException or
            InvalidOperationException or
            KeyNotFoundException)
        {
            Logger.LogError(exception, "Qwen3.8 benchmark run failed for profile {ProfileId}, run {RunId}.", profile.Id, currentRun);
            errorMessage = $"Benchmark-Lauf fehlgeschlagen: {exception.Message}";
            failureMessage = exception.Message;
        }
        finally
        {
            if (savedRun is not null)
            {
                try
                {
                    savedRun = await DataService.BenchmarkRun_UpdateAsync(
                        savedRun with { State = finalState, ErrorMessage = failureMessage },
                        CancellationToken.None);
                }
                catch (Exception exception) when (exception is HubException or HttpRequestException or InvalidOperationException or KeyNotFoundException or OperationCanceledException)
                {
                    Logger.LogError(exception, "Could not finalize persisted benchmark run {BenchmarkRunId}.", savedRun.Id);
                    errorMessage = string.IsNullOrWhiteSpace(errorMessage)
                        ? $"Der Benchmark wurde ausgeführt, aber sein Abschlussstatus konnte nicht gespeichert werden: {exception.Message}"
                        : $"{errorMessage} Der Abschlussstatus konnte ebenfalls nicht gespeichert werden: {exception.Message}";
                    seriesCompleted = false;
                }
            }
            isRunning = false;
            activeRunKind = string.Empty;
            runCancellation?.Dispose();
            runCancellation = null;
        }
        return seriesCompleted;
    }

    private void CancelRun() => runCancellation?.Cancel();

    private void ClearResults()
    {
        results.Clear();
        shortSummary = null;
        shortBenchmarkProfileId = null;
        shortBenchmarkPassed = false;
        errorMessage = null;
        statusMessage = null;
    }

    private void SelectHistoryModel(ChangeEventArgs args)
    {
        historyModelPath = string.IsNullOrWhiteSpace(args.Value?.ToString()) ? null : args.Value.ToString();
        if (historyProfileId is Guid profileId && !HistoryProfiles.Any(profile => profile.ProfileId == profileId))
            historyProfileId = null;
    }

    private void SelectHistoryProfile(ChangeEventArgs args) =>
        historyProfileId = Guid.TryParse(args.Value?.ToString(), out var id) &&
            HistoryProfiles.Any(profile => profile.ProfileId == id)
            ? id
            : null;

    private async Task DeleteHistoryRunAsync(Guid id)
    {
        try
        {
            await DataService.BenchmarkRun_DeleteAsync(id);
        }
        catch (Exception exception) when (exception is HubException or HttpRequestException or InvalidOperationException or KeyNotFoundException)
        {
            Logger.LogError(exception, "Could not delete benchmark history entry {BenchmarkRunId}.", id);
            errorMessage = $"Der gespeicherte Benchmark-Lauf konnte nicht gelöscht werden: {exception.Message}";
        }
    }

    private Task HandleBenchmarkRunReadAsync(IReadOnlyList<BenchmarkRun> runs) => InvokeAsync(() =>
    {
        history.Clear();
        history.AddRange(runs);
        StateHasChanged();
    });

    private Task HandleBenchmarkRunChangedAsync(BenchmarkRun run) => InvokeAsync(() =>
    {
        var index = history.FindIndex(existing => existing.Id == run.Id);
        if (index < 0)
            history.Add(run);
        else
            history[index] = run;
        history.Sort((left, right) => right.CreatedAtUtc.CompareTo(left.CreatedAtUtc));
        StateHasChanged();
    });

    private Task HandleBenchmarkRunDeletedAsync(BenchmarkRun run) => InvokeAsync(() =>
    {
        history.RemoveAll(existing => existing.Id == run.Id);
        StateHasChanged();
    });

    private Task HandleLoadedModelChangedAsync(ModelLoadStatus status) => InvokeAsync(() =>
    {
        loadStatus = status;
        StateHasChanged();
    });

    private static Qwen38BenchmarkSummary CreateSummary(IReadOnlyList<BenchmarkMeasurement> measured)
    {
        var measuredRuns = measured
            .Where(result => result.Kind == "measured")
            .ToArray();
        if (measuredRuns.Length == 0)
            measuredRuns = measured.Where(result => result.Kind == "long").ToArray();
        var rates = measuredRuns
            .Where(result => result.ClientDecodeTokensPerSecond is not null)
            .Select(result => result.ClientDecodeTokensPerSecond!.Value)
            .Order()
            .ToArray();
        var serverRates = measuredRuns
            .Where(result => result.ServerTokensPerSecond is not null)
            .Select(result => result.ServerTokensPerSecond!.Value)
            .Order()
            .ToArray();
        return new Qwen38BenchmarkSummary(
            Median(rates),
            Median(serverRates),
            measuredRuns.Length > 0 && measuredRuns.All(result => result.CompletionLengthMatchesLimit));
    }

    private static string GetBackendDisplayName(ConfigurationBackend backend) => backend switch
    {
        ConfigurationBackend.Llama => "LLama.cpp",
        ConfigurationBackend.OpenVino => "OpenVINO",
        ConfigurationBackend.Vllm => "vLLM",
        _ => backend.ToString()
    };

    private static string GetRunStateDisplayName(BenchmarkRun run) => run.State switch
    {
        BenchmarkRunState.Running => "Läuft",
        BenchmarkRunState.Completed => "Abgeschlossen",
        BenchmarkRunState.Cancelled => "Abgebrochen",
        BenchmarkRunState.Failed => "Fehlgeschlagen",
        _ => run.State.ToString()
    };

    private static string FormatTimestamp(DateTime timestamp) =>
        timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture);

    private static string GetSnapshotKey(Guid id) => $"{id}-snapshot";

    private static string FormatSettings(BenchmarkRunSettings settings) =>
        System.Text.Json.JsonSerializer.Serialize(
            settings,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    private static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return null;

        var middle = values.Count / 2;
        return Math.Round(values.Count % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2
            : values[middle], 2, MidpointRounding.AwayFromZero);
    }

    private static string FormatRate(double? rate) => rate is double value
        ? $"{value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} tok/s"
        : "—";

    private static string FormatMilliseconds(double? milliseconds) => milliseconds is double value
        ? $"{value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} ms"
        : "—";

    private static string FormatMilliseconds(double milliseconds) =>
        $"{milliseconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} ms";

    public ValueTask DisposeAsync()
    {
        RuntimeEvents.LoadedModel_Create -= HandleLoadedModelChangedAsync;
        RuntimeEvents.LoadedModel_Update -= HandleLoadedModelChangedAsync;
        RuntimeEvents.LoadedModel_Delete -= HandleLoadedModelChangedAsync;
        BenchmarkEvents.BenchmarkRun_Read -= HandleBenchmarkRunReadAsync;
        BenchmarkEvents.BenchmarkRun_Create -= HandleBenchmarkRunChangedAsync;
        BenchmarkEvents.BenchmarkRun_Update -= HandleBenchmarkRunChangedAsync;
        BenchmarkEvents.BenchmarkRun_Delete -= HandleBenchmarkRunDeletedAsync;
        runCancellation?.Cancel();
        runCancellation?.Dispose();
        return ValueTask.CompletedTask;
    }
}
