namespace Esi.AI.Models;

/// <summary>Describes the lifecycle state of a PyTorch training run.</summary>
public enum TrainingRunState
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>Specifies the inputs and bounded settings for a QLoRA training run.</summary>
public sealed record CreateTrainingRunRequest
{
    /// <summary>Gets or sets the local JSONL dataset path.</summary>
    public string DatasetPath { get; init; } = string.Empty;

    /// <summary>Gets or sets the number of training epochs. The default is one.</summary>
    public int Epochs { get; init; } = 1;

    /// <summary>Gets or sets the tokenized sequence limit. The default is 1024.</summary>
    public int MaxSequenceLength { get; init; } = 1024;

    /// <summary>Gets or sets the per-device batch size. The default is one.</summary>
    public int BatchSize { get; init; } = 1;

    /// <summary>Gets or sets the gradient accumulation count. The default is four.</summary>
    public int GradientAccumulationSteps { get; init; } = 4;

    /// <summary>Gets or sets the LoRA rank. The default is eight.</summary>
    public int LoraRank { get; init; } = 8;

    /// <summary>Gets or sets the learning rate. The default is 0.0002.</summary>
    public double LearningRate { get; init; } = 0.0002;
}

/// <summary>Reports the server-owned progress and output of a training run.</summary>
public sealed record TrainingRunStatus(
    Guid Id,
    string ModelId,
    string DatasetPath,
    string OutputDirectory,
    TrainingRunState State,
    int CurrentStep,
    int? TotalSteps,
    double? Loss,
    string Log,
    string? Error,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);