using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

public sealed class LlamaAdvancedState
{
    public int SeqMax { get; set; } = 1;
    public int RecurrentRollbackSnapshots { get; set; }
    public bool UseMemorymap { get; set; } = true;
    public bool UseDirectIO { get; set; }
    public bool UseMemoryLock { get; set; }
    public int? Threads { get; set; }
    public int? BatchThreads { get; set; }
    public int BatchSize { get; set; } = 512;
    public int UBatchSize { get; set; } = 512;
    public bool Embeddings { get; set; }
    public bool NoKqvOffload { get; set; }
    public bool FlashAttention { get; set; }
    public bool VocabOnly { get; set; }
    public bool OpOffload { get; set; }
    public bool SwaFull { get; set; }
    public bool KVUnified { get; set; }
    public float? RopeFrequencyBase { get; set; }
    public float? RopeFrequencyScale { get; set; }
    public float? YarnExtrapolationFactor { get; set; }
    public float? YarnAttentionFactor { get; set; }
    public float? YarnBetaFast { get; set; }
    public float? YarnBetaSlow { get; set; }
    public int? YarnOriginalContext { get; set; }
    public string ContextType { get; set; } = "Default";
    public string? TypeK { get; set; }
    public string? TypeV { get; set; }
    public string PoolingType { get; set; } = "Unspecified";
    public string AttentionType { get; set; } = "Unspecified";
    public string? YarnScalingType { get; set; }
    public bool CheckTensors { get; set; }
    public float Temperature { get; set; } = .75f;
    public int TopK { get; set; } = 40;
    public float TopP { get; set; } = .9f;
    public float MinP { get; set; } = .1f;
    public float RepeatPenalty { get; set; } = 1f;
    public float FrequencyPenalty { get; set; }
    public float PresencePenalty { get; set; }
    public int PenaltyCount { get; set; } = 64;
    public int MaxTokens { get; set; } = -1;
    public int TokensKeep { get; set; }
    public int Seed { get; set; }
    public bool DecodeSpecialTokens { get; set; }

    public void Load(LlamaAdvancedSettings? settings)
    {
        if (settings is null) return;
        SeqMax = (int)settings.SeqMax; RecurrentRollbackSnapshots = (int)settings.RecurrentRollbackSnapshots;
        UseMemorymap = settings.UseMemorymap; UseDirectIO = settings.UseDirectIO; UseMemoryLock = settings.UseMemoryLock;
        Threads = settings.Threads; BatchThreads = settings.BatchThreads; BatchSize = (int)settings.BatchSize; UBatchSize = (int)settings.UBatchSize;
        Embeddings = settings.Embeddings; NoKqvOffload = settings.NoKqvOffload; FlashAttention = settings.FlashAttention ?? false;
        VocabOnly = settings.VocabOnly; OpOffload = settings.OpOffload ?? false; SwaFull = settings.SwaFull ?? false; KVUnified = settings.KVUnified ?? false;
        RopeFrequencyBase = settings.RopeFrequencyBase; RopeFrequencyScale = settings.RopeFrequencyScale; YarnExtrapolationFactor = settings.YarnExtrapolationFactor;
        YarnAttentionFactor = settings.YarnAttentionFactor; YarnBetaFast = settings.YarnBetaFast; YarnBetaSlow = settings.YarnBetaSlow;
        YarnOriginalContext = settings.YarnOriginalContext.HasValue ? (int)settings.YarnOriginalContext.Value : null; ContextType = settings.ContextType;
        TypeK = settings.TypeK; TypeV = settings.TypeV; PoolingType = settings.PoolingType; AttentionType = settings.AttentionType; YarnScalingType = settings.YarnScalingType;
        CheckTensors = settings.CheckTensors; Temperature = settings.Temperature; TopK = settings.TopK; TopP = settings.TopP; MinP = settings.MinP;
        RepeatPenalty = settings.RepeatPenalty; FrequencyPenalty = settings.FrequencyPenalty; PresencePenalty = settings.PresencePenalty;
        PenaltyCount = settings.PenaltyCount; MaxTokens = settings.MaxTokens; TokensKeep = settings.TokensKeep;
        Seed = settings.Seed > int.MaxValue ? int.MaxValue : (int)settings.Seed; DecodeSpecialTokens = settings.DecodeSpecialTokens;
    }

    public LlamaAdvancedSettings ToSettings() => new((uint)Math.Max(1, SeqMax), (uint)Math.Max(0, RecurrentRollbackSnapshots), UseMemorymap, UseDirectIO, UseMemoryLock, Threads, BatchThreads, (uint)Math.Max(1, BatchSize), (uint)Math.Max(1, UBatchSize), Embeddings, NoKqvOffload, FlashAttention, VocabOnly, OpOffload, SwaFull, KVUnified, RopeFrequencyBase, RopeFrequencyScale, YarnExtrapolationFactor, YarnAttentionFactor, YarnBetaFast, YarnBetaSlow, YarnOriginalContext.HasValue ? (uint?)Math.Max(1, YarnOriginalContext.Value) : null, ContextType, TypeK, TypeV, PoolingType, AttentionType, YarnScalingType, CheckTensors, Temperature, TopK, TopP, MinP, RepeatPenalty, FrequencyPenalty, PresencePenalty, PenaltyCount, MaxTokens, TokensKeep, (uint)Math.Max(0, Seed), DecodeSpecialTokens);
}