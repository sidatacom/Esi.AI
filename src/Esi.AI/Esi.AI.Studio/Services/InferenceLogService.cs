using Esi.AI.Models;
using Esi.AI.Studio.Data;
using Esi.AI.Studio.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Esi.AI.Studio.Services;

/// <summary>Publishes persisted inference-log collection changes over SignalR.</summary>
public interface IInferenceLogPublisher
{
    /// <summary>Publishes a newly persisted inference log.</summary>
    Task PublishCreateAsync(InferenceLog log, CancellationToken cancellationToken = default);

    /// <summary>Publishes an updated inference log.</summary>
    Task PublishUpdateAsync(InferenceLog log, CancellationToken cancellationToken = default);

    /// <summary>Publishes a deleted inference log.</summary>
    Task PublishDeleteAsync(InferenceLog log, CancellationToken cancellationToken = default);
}

internal sealed class SignalRInferenceLogPublisher(IHubContext<DataHub> hubContext) : IInferenceLogPublisher
{
    public Task PublishCreateAsync(InferenceLog log, CancellationToken cancellationToken = default) =>
        hubContext.Clients.All.SendAsync("InferenceLog_Create", log, cancellationToken);

    public Task PublishUpdateAsync(InferenceLog log, CancellationToken cancellationToken = default) =>
        hubContext.Clients.All.SendAsync("InferenceLog_Update", log, cancellationToken);

    public Task PublishDeleteAsync(InferenceLog log, CancellationToken cancellationToken = default) =>
        hubContext.Clients.All.SendAsync("InferenceLog_Delete", log, cancellationToken);
}

/// <summary>Persists payload-free inference metadata and publishes collection changes.</summary>
public sealed class InferenceLogService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IInferenceLogPublisher publisher)
{
    private const int MaximumEntries = 500;
    private const int MaximumErrorMessageLength = 2048;
    private readonly SemaphoreSlim persistenceLock = new(1, 1);

    internal event Func<IReadOnlyList<InferenceLog>, Task>? Read;
    internal event Func<InferenceLog, Task>? Created;
    internal event Func<InferenceLog, Task>? Updated;
    internal event Func<InferenceLog, Task>? Deleted;

    /// <summary>Creates the pending entry before an inference operation begins.</summary>
    public async Task<InferenceLog> InferenceLog_CreateAsync(InferenceLog log, CancellationToken cancellationToken = default)
    {
        Validate(log);
        if (log.Status != InferenceLogStatus.Running)
            throw new ArgumentException("A new inference log must be in the running state.", nameof(log));
        if (log.Id == Guid.Empty)
            throw new ArgumentException("An inference log identifier is required.", nameof(log));

        InferenceLog created;
        InferenceLog[] deleted;
        await persistenceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            if (await db.InferenceLogs.AnyAsync(item => item.Id == log.Id || item.InferenceId == log.InferenceId, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException($"An inference log already exists for '{log.InferenceId}'.");

            var entity = ToEntity(log);
            db.InferenceLogs.Add(entity);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            created = ToContract(entity);

            deleted = await TrimExcessEntriesAsync(db, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            persistenceLock.Release();
        }

        foreach (var removed in deleted)
        {
            await publisher.PublishDeleteAsync(removed, cancellationToken).ConfigureAwait(false);
            await NotifyAsync(Deleted, removed).ConfigureAwait(false);
        }
        await publisher.PublishCreateAsync(created, cancellationToken).ConfigureAwait(false);
        await NotifyAsync(Created, created).ConfigureAwait(false);
        return created;
    }

    /// <summary>Reads the latest persisted inference logs, newest first.</summary>
    public async Task<IReadOnlyList<InferenceLog>> InferenceLog_ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entities = await db.InferenceLogs.AsNoTracking()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        var logs = entities
            .OrderByDescending(item => item.StartedAtUtc)
            .ThenByDescending(item => item.Id)
            .Take(MaximumEntries)
            .Select(ToContract)
            .ToArray();
        await NotifyAsync(Read, logs).ConfigureAwait(false);
        return logs;
    }

    /// <summary>Updates a running inference entry with its estimate or terminal outcome.</summary>
    public async Task<InferenceLog> InferenceLog_UpdateAsync(InferenceLog log, CancellationToken cancellationToken = default)
    {
        Validate(log);
        if (log.Id == Guid.Empty)
            throw new ArgumentException("A persisted inference log identifier is required.", nameof(log));

        InferenceLog updated;
        InferenceLog[] deleted;
        await persistenceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var entity = await db.InferenceLogs.SingleOrDefaultAsync(item => item.Id == log.Id, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Inference log '{log.Id}' was not found.");
            var previous = ToContract(entity);
            if (previous.Status != InferenceLogStatus.Running)
                throw new InvalidOperationException("Only a running inference log can be updated.");
            if (previous with { EstimatedPrefillTokens = log.EstimatedPrefillTokens, OutputTokenCount = log.OutputTokenCount,
                    FinishReason = log.FinishReason, Status = log.Status, CompletedAtUtc = log.CompletedAtUtc,
                    DurationMilliseconds = log.DurationMilliseconds, ErrorType = log.ErrorType, ErrorMessage = log.ErrorMessage } != log)
                throw new InvalidOperationException("Inference request metadata cannot be changed after creation.");
            if (!Enum.IsDefined(log.Status))
                throw new ArgumentOutOfRangeException(nameof(log), "The inference log status is invalid.");
            if (log.Status != InferenceLogStatus.Running && (log.CompletedAtUtc is null || log.DurationMilliseconds is null))
                throw new ArgumentException("A terminal inference log must include its completion time and duration.", nameof(log));

            entity.Status = log.Status;
            entity.EstimatedPrefillTokens = log.EstimatedPrefillTokens;
            entity.OutputTokenCount = log.OutputTokenCount;
            entity.FinishReason = log.FinishReason;
            entity.CompletedAtUtc = log.CompletedAtUtc;
            entity.DurationMilliseconds = log.DurationMilliseconds;
            entity.ErrorType = log.ErrorType;
            entity.ErrorMessage = LimitErrorMessage(log.ErrorMessage);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            updated = ToContract(entity);
            deleted = log.Status == InferenceLogStatus.Running
                ? []
                : await TrimExcessEntriesAsync(db, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            persistenceLock.Release();
        }

        foreach (var removed in deleted)
        {
            await publisher.PublishDeleteAsync(removed, cancellationToken).ConfigureAwait(false);
            await NotifyAsync(Deleted, removed).ConfigureAwait(false);
        }
        await publisher.PublishUpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        await NotifyAsync(Updated, updated).ConfigureAwait(false);
        return updated;
    }

    /// <summary>Deletes one completed inference log.</summary>
    public async Task InferenceLog_DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("An inference log identifier is required.", nameof(id));

        InferenceLog deleted;
        await persistenceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var entity = await db.InferenceLogs.SingleOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Inference log '{id}' was not found.");
            if (entity.Status == InferenceLogStatus.Running)
                throw new InvalidOperationException("A running inference log cannot be deleted.");

            deleted = ToContract(entity);
            db.InferenceLogs.Remove(entity);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            persistenceLock.Release();
        }

        await publisher.PublishDeleteAsync(deleted, cancellationToken).ConfigureAwait(false);
        await NotifyAsync(Deleted, deleted).ConfigureAwait(false);
    }

    /// <summary>Marks requests left running by a previous Studio process as interrupted.</summary>
    public async Task InferenceLog_MarkInterruptedAsync(CancellationToken cancellationToken = default)
    {
        await persistenceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var running = await db.InferenceLogs
                .Where(item => item.Status == InferenceLogStatus.Running)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            foreach (var entity in running)
            {
                entity.Status = InferenceLogStatus.Interrupted;
                entity.CompletedAtUtc = now;
                entity.DurationMilliseconds = Math.Max(0, (long)(now - entity.StartedAtUtc).TotalMilliseconds);
                entity.ErrorType = "StudioProcessRestarted";
                entity.ErrorMessage = "Studio restarted before the inference request completed.";
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            persistenceLock.Release();
        }
    }

    private static void Validate(InferenceLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (string.IsNullOrWhiteSpace(log.InferenceId) ||
            string.IsNullOrWhiteSpace(log.Path) ||
            string.IsNullOrWhiteSpace(log.Backend) ||
            string.IsNullOrWhiteSpace(log.Model))
            throw new ArgumentException("Inference identifier, path, backend, and model are required.", nameof(log));
        if (log.MessageCount < 0 || log.ImageCount < 0 || log.ToolCount < 0 ||
            log.EstimatedPrefillTokens < 0 || log.OutputTokenCount < 0 || log.DurationMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(log), "Inference log counts and duration must be non-negative.");
    }

    private static InferenceLogEntity ToEntity(InferenceLog log) =>
        new()
        {
            Id = log.Id,
            InferenceId = log.InferenceId,
            StartedAtUtc = log.StartedAtUtc,
            Path = log.Path,
            Backend = log.Backend,
            Model = log.Model,
            BackendVariantId = log.BackendVariantId,
            MessageCount = log.MessageCount,
            ImageCount = log.ImageCount,
            ToolCount = log.ToolCount,
            Streaming = log.Streaming,
            Status = log.Status,
            EstimatedPrefillTokens = log.EstimatedPrefillTokens,
            OutputTokenCount = log.OutputTokenCount,
            FinishReason = log.FinishReason,
            CompletedAtUtc = log.CompletedAtUtc,
            DurationMilliseconds = log.DurationMilliseconds,
            ErrorType = log.ErrorType,
            ErrorMessage = LimitErrorMessage(log.ErrorMessage)
        };

    private static InferenceLog ToContract(InferenceLogEntity entity) =>
        new(
            entity.Id,
            entity.InferenceId,
            entity.StartedAtUtc,
            entity.Path,
            entity.Backend,
            entity.Model,
            entity.BackendVariantId,
            entity.MessageCount,
            entity.ImageCount,
            entity.ToolCount,
            entity.Streaming,
            entity.Status,
            entity.EstimatedPrefillTokens,
            entity.OutputTokenCount,
            entity.FinishReason,
            entity.CompletedAtUtc,
            entity.DurationMilliseconds,
            entity.ErrorType,
            entity.ErrorMessage);

    private static string? LimitErrorMessage(string? message) =>
        message is { Length: > MaximumErrorMessageLength } ? message[..MaximumErrorMessageLength] : message;

    private static async Task<InferenceLog[]> TrimExcessEntriesAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var entries = await db.InferenceLogs.ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var completedEntryLimit = Math.Max(0, MaximumEntries - entries.Count(item => item.Status == InferenceLogStatus.Running));
        var excessEntities = entries
            .Where(item => item.Status != InferenceLogStatus.Running)
            .OrderByDescending(item => item.StartedAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip(completedEntryLimit)
            .ToArray();
        if (excessEntities.Length == 0)
            return [];

        db.InferenceLogs.RemoveRange(excessEntities);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return excessEntities.Select(ToContract).ToArray();
    }

    private static async Task NotifyAsync<T>(Func<T, Task>? handler, T value)
    {
        if (handler is not null)
            await handler(value).ConfigureAwait(false);
    }
}
