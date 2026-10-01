using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Esi.AI.Models;

namespace Esi.AI.PyTorch;

/// <summary>Runs a single local QLoRA job and publishes its lifecycle to the Studio host.</summary>
public sealed class PyTorchTrainingService(
    PyTorchTrainingOptions options,
    ITrainingRunStatusPublisher publisher,
    IPythonTrainingProcessRunner processRunner) : IPyTorchTrainingService, IAsyncDisposable
{
    public const string ModelId = "Qwen/Qwen3-4B-Instruct-2507";

    private const int MaxLogCharacters = 12_000;
    private readonly object sync = new();
    private readonly Dictionary<Guid, TrainingRunExecution> runs = [];
    private Guid? activeRunId;

    /// <inheritdoc />
    public async Task<TrainingRunStatus> TrainingRun_CreateAsync(
        CreateTrainingRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var validationErrors = TrainingRunRequestValidator.Validate(request);
        if (validationErrors.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, validationErrors), nameof(request));

        TrainingRunExecution execution;
        lock (sync)
        {
            if (activeRunId is not null)
                throw new InvalidOperationException("A PyTorch training run is already active.");

            var now = DateTimeOffset.UtcNow;
            var id = Guid.NewGuid();
            var datasetPath = Path.GetFullPath(request.DatasetPath);
            var outputDirectory = Path.Combine(Path.GetFullPath(options.OutputDirectory), id.ToString("N"));
            execution = new TrainingRunExecution(
                request with { DatasetPath = datasetPath },
                new TrainingRunStatus(
                    id,
                    ModelId,
                    datasetPath,
                    outputDirectory,
                    TrainingRunState.Pending,
                    0,
                    null,
                    null,
                    "Waiting for the Python XPU worker to start.",
                    null,
                    now,
                    now));
            runs.Add(id, execution);
            activeRunId = id;
        }

        try
        {
            await publisher.PublishCreateAsync(execution.Status, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (sync)
            {
                runs.Remove(execution.Status.Id);
                activeRunId = null;
            }

            throw;
        }

        execution.Worker = Task.Run(() => ExecuteAsync(execution), CancellationToken.None);
        return execution.Status;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TrainingRunStatus>> TrainingRun_ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            IReadOnlyList<TrainingRunStatus> statuses = runs.Values
                .Select(run => run.Status)
                .OrderByDescending(status => status.CreatedAtUtc)
                .ToArray();
            return Task.FromResult(statuses);
        }
    }

    /// <inheritdoc />
    public async Task<TrainingRunStatus?> TrainingRun_UpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TrainingRunStatus? status;
        lock (sync)
            status = runs.GetValueOrDefault(id)?.Status;

        if (status is not null)
            await publisher.PublishUpdateAsync(status, cancellationToken).ConfigureAwait(false);

        return status;
    }

    /// <inheritdoc />
    public async Task TrainingRun_DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        TrainingRunExecution execution;
        TrainingRunStatus? deletedStatus = null;
        Task? worker = null;
        lock (sync)
        {
            if (!runs.TryGetValue(id, out var found))
                return;
            execution = found;
        }

        await execution.PublishLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (sync)
            {
                if (!runs.ContainsKey(id))
                    return;

                if (execution.Status.State is TrainingRunState.Pending or TrainingRunState.Running)
                {
                    execution.Cancellation.Cancel();
                    worker = execution.Worker;
                }
                else
                {
                    runs.Remove(id);
                    deletedStatus = execution.Status;
                }
            }

            if (deletedStatus is not null)
                await publisher.PublishDeleteAsync(deletedStatus, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            execution.PublishLock.Release();
        }

        if (worker is not null)
            await worker.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> TrainingRun_SampleDataset_CreateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "Python", "sample_train.jsonl");
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The packaged sample training dataset was not deployed.", sourcePath);

        var localDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localDataDirectory))
            localDataDirectory = Path.GetTempPath();

        var destinationPath = Path.Combine(localDataDirectory, "Esi.AI", "PyTorch", "datasets", "sample_train.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var source = File.OpenRead(sourcePath);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        return destinationPath;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        TrainingRunExecution[] activeRuns;
        lock (sync)
            activeRuns = runs.Values.Where(run => run.Status.State is TrainingRunState.Pending or TrainingRunState.Running).ToArray();

        foreach (var run in activeRuns)
            run.Cancellation.Cancel();

        var workers = activeRuns.Select(run => run.Worker).Where(worker => worker is not null).Cast<Task>();
        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private async Task ExecuteAsync(TrainingRunExecution execution)
    {
        try
        {
            execution.Cancellation.Token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(execution.Status.OutputDirectory);
            var scriptPath = options.TrainerScriptPath ?? Path.Combine(AppContext.BaseDirectory, "Python", "train_qlora.py");
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException("The packaged PyTorch QLoRA trainer was not deployed.", scriptPath);

            var startInfo = BuildStartInfo(execution.Request, execution.Status.OutputDirectory, scriptPath, options.PythonExecutable);
            await PublishStatusAsync(execution, current => current with
            {
                State = TrainingRunState.Running,
                Log = "Python worker started. Loading Qwen3-4B-Instruct-2507 and checking torch.xpu...",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            }).ConfigureAwait(false);

            var exitCode = await processRunner.RunAsync(
                startInfo,
                line => CaptureOutputLineAsync(line, execution),
                execution.Cancellation.Token).ConfigureAwait(false);

            if (exitCode != 0)
                throw new InvalidOperationException($"Python training exited with code {exitCode}.");

            await PublishStatusAsync(execution, current => current with
            {
                State = TrainingRunState.Completed,
                Log = AppendLog(current.Log, "Adapter training completed."),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            }).ConfigureAwait(false);
            lock (sync)
                activeRunId = null;
        }
        catch (OperationCanceledException) when (execution.Cancellation.IsCancellationRequested)
        {
            await RemoveAndPublishDeleteAsync(execution, TrainingRunState.Cancelled, null).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await PublishStatusAsync(execution, current => current with
            {
                State = TrainingRunState.Failed,
                Error = exception.Message,
                Log = AppendLog(current.Log, exception.Message),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            }).ConfigureAwait(false);
            await RemoveAndPublishDeleteAsync(execution, TrainingRunState.Failed, exception.Message).ConfigureAwait(false);
        }
        finally
        {
            execution.Cancellation.Dispose();
        }
    }

    private Task CaptureOutputLineAsync(string line, TrainingRunExecution execution) =>
        PublishStatusAsync(execution, current => ApplyOutputLine(current, line));

    private async Task PublishStatusAsync(TrainingRunExecution execution, Func<TrainingRunStatus, TrainingRunStatus> update)
    {
        await execution.PublishLock.WaitAsync().ConfigureAwait(false);
        try
        {
            TrainingRunStatus status;
            lock (sync)
            {
                if (!runs.ContainsKey(execution.Status.Id))
                    return;

                status = update(execution.Status);
                execution.Status = status;
            }

            await publisher.PublishUpdateAsync(status, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            execution.PublishLock.Release();
        }
    }

    private async Task RemoveAndPublishDeleteAsync(TrainingRunExecution execution, TrainingRunState state, string? error)
    {
        await execution.PublishLock.WaitAsync().ConfigureAwait(false);
        try
        {
            TrainingRunStatus deletedStatus;
            lock (sync)
            {
                deletedStatus = execution.Status with
                {
                    State = state,
                    Error = error,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
                runs.Remove(deletedStatus.Id);
                if (activeRunId == deletedStatus.Id)
                    activeRunId = null;
            }

            await publisher.PublishDeleteAsync(deletedStatus, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            execution.PublishLock.Release();
        }
    }

    internal static ProcessStartInfo BuildStartInfo(
        CreateTrainingRunRequest request,
        string outputDirectory,
        string scriptPath,
        string pythonExecutable)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExecutable,
            WorkingDirectory = outputDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in new[]
        {
            scriptPath,
            "--model-id", ModelId,
            "--dataset", request.DatasetPath,
            "--output-dir", outputDirectory,
            "--epochs", request.Epochs.ToString(CultureInfo.InvariantCulture),
            "--max-sequence-length", request.MaxSequenceLength.ToString(CultureInfo.InvariantCulture),
            "--batch-size", request.BatchSize.ToString(CultureInfo.InvariantCulture),
            "--gradient-accumulation-steps", request.GradientAccumulationSteps.ToString(CultureInfo.InvariantCulture),
            "--lora-rank", request.LoraRank.ToString(CultureInfo.InvariantCulture),
            "--learning-rate", request.LearningRate.ToString("R", CultureInfo.InvariantCulture)
        })
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    private static TrainingRunStatus ApplyOutputLine(TrainingRunStatus status, string line)
    {
        var currentStep = status.CurrentStep;
        var totalSteps = status.TotalSteps;
        var loss = status.Loss;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == "progress")
            {
                if (root.TryGetProperty("step", out var step))
                    currentStep = step.GetInt32();
                if (root.TryGetProperty("total_steps", out var total))
                    totalSteps = total.GetInt32();
                if (root.TryGetProperty("loss", out var lossValue) && lossValue.ValueKind == JsonValueKind.Number)
                    loss = lossValue.GetDouble();
            }
        }
        catch (JsonException)
        {
        }

        return status with
        {
            CurrentStep = currentStep,
            TotalSteps = totalSteps,
            Loss = loss,
            Log = AppendLog(status.Log, line),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static string AppendLog(string log, string line)
    {
        var combined = string.Concat(log, Environment.NewLine, line);
        return combined.Length <= MaxLogCharacters ? combined : combined[^MaxLogCharacters..];
    }

    private sealed class TrainingRunExecution(CreateTrainingRunRequest request, TrainingRunStatus status)
    {
        public CreateTrainingRunRequest Request { get; } = request;
        public TrainingRunStatus Status { get; set; } = status;
        public CancellationTokenSource Cancellation { get; } = new();
        public SemaphoreSlim PublishLock { get; } = new(1, 1);
        public Task? Worker { get; set; }
    }
}