using Esi.AI.Models;

namespace Esi.AI.PyTorch;

/// <summary>Validates local dataset paths and safe QLoRA test-run parameters.</summary>
public static class TrainingRunRequestValidator
{
    /// <summary>Returns validation errors for a requested training run.</summary>
    /// <param name="request">The requested training settings.</param>
    /// <param name="fileExists">An optional file-existence check for isolated tests.</param>
    /// <returns>A list of validation errors, empty when the request is valid.</returns>
    public static IReadOnlyList<string> Validate(CreateTrainingRunRequest request, Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        fileExists ??= File.Exists;

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.DatasetPath))
        {
            errors.Add("A JSONL dataset path is required.");
        }
        else
        {
            try
            {
                var datasetPath = Path.GetFullPath(request.DatasetPath);
                if (!Path.GetExtension(datasetPath).Equals(".jsonl", StringComparison.OrdinalIgnoreCase))
                    errors.Add("The dataset must be a .jsonl file.");
                else if (!fileExists(datasetPath))
                    errors.Add("The JSONL dataset file does not exist.");
            }
            catch (ArgumentException)
            {
                errors.Add("The dataset path is invalid.");
            }
            catch (NotSupportedException)
            {
                errors.Add("The dataset path is invalid.");
            }
            catch (PathTooLongException)
            {
                errors.Add("The dataset path is invalid.");
            }
        }

        if (request.Epochs is < 1 or > 5)
            errors.Add("Epochs must be between 1 and 5.");
        if (request.MaxSequenceLength is < 256 or > 8192)
            errors.Add("Maximum sequence length must be between 256 and 8192 tokens.");
        if (request.BatchSize is < 1 or > 4)
            errors.Add("Batch size must be between 1 and 4.");
        if (request.GradientAccumulationSteps is < 1 or > 64)
            errors.Add("Gradient accumulation steps must be between 1 and 64.");
        if (request.LoraRank is < 4 or > 64)
            errors.Add("LoRA rank must be between 4 and 64.");
        if (!double.IsFinite(request.LearningRate) || request.LearningRate is < 0.000001 or > 0.0005)
            errors.Add("Learning rate must be between 0.000001 and 0.0005.");

        return errors;
    }
}