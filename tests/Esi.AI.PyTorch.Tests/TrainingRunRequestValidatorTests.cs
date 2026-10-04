using Esi.AI.Models;
using Esi.AI.PyTorch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.PyTorch.Tests;

[TestClass]
public sealed class TrainingRunRequestValidatorTests
{
    [TestMethod]
    public void Validate_ExistingJsonlAndDefaultSettings_ReturnsNoErrors()
    {
        var request = new CreateTrainingRunRequest { DatasetPath = "dataset.jsonl" };

        var errors = TrainingRunRequestValidator.Validate(request, _ => true);

        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    public void Validate_MissingDatasetAndOutOfRangeSettings_ReturnsErrors()
    {
        var request = new CreateTrainingRunRequest
        {
            DatasetPath = "missing.jsonl",
            Epochs = 0,
            MaxSequenceLength = 128,
            BatchSize = 5,
            LoraRank = 2,
            LearningRate = double.NaN
        };

        var errors = TrainingRunRequestValidator.Validate(request, _ => false);

        Assert.AreEqual(6, errors.Count);
    }

    [TestMethod]
    public void Validate_NonJsonlDataset_ReturnsFormatError()
    {
        var request = new CreateTrainingRunRequest { DatasetPath = "dataset.csv" };

        var errors = TrainingRunRequestValidator.Validate(request, _ => true);

        Assert.Contains("The dataset must be a .jsonl file.", errors);
    }
}