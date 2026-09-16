using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Egil.Orleans.Messaging.Outboxes;

internal sealed class OutboxDispatcher<TOutbox>(
    OutboxPostmanRegistry<TOutbox> postmen,
    ILogger logger,
    string grainType,
    TimeProvider timeProvider,
    Func<TOutbox, Type> getMessageType,
    Func<TOutbox, string?> getTraceParent)
    where TOutbox : notnull
{
    public async Task<ImmutableArray<OutboxDispatchResult<TOutbox>>> DispatchAsync(
        ImmutableArray<TOutbox> pending,
        TimeSpan processingTimeout,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(processingTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var started = Stopwatch.GetTimestamp();

        try
        {
            var groups = CreateDispatchGroups(pending, linked.Token);
            var dispatchTasks = new Task<ImmutableArray<OutboxDispatchResult<TOutbox>>>[groups.Count];
            for (var i = 0; i < groups.Count; i++)
            {
                dispatchTasks[i] = DispatchGroupAsync(groups[i], linked.Token);
            }

            var groupResults = await Task.WhenAll(dispatchTasks);
            var results = ImmutableArray.CreateBuilder<OutboxDispatchResult<TOutbox>>(pending.Length);
            foreach (var groupResult in groupResults)
            {
                results.AddRange(groupResult);
            }

            return results.ToImmutable();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"Outbox post run exceeded the configured timeout of {processingTimeout}.");
        }
        finally
        {
            MessagingTelemetry.RecordOutboxPostDuration(
                grainType,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private List<DispatchGroup> CreateDispatchGroups(
        ImmutableArray<TOutbox> pending,
        CancellationToken cancellationToken)
    {
        var groups = new List<DispatchGroup>();
        foreach (var item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var postman = postmen.Find(item);
            var group = groups.FirstOrDefault(candidate => candidate.Postman == postman);
            if (group is null)
            {
                group = new DispatchGroup(postman);
                groups.Add(group);
            }

            group.Items.Add(item);
        }

        return groups;
    }

    private async Task<ImmutableArray<OutboxDispatchResult<TOutbox>>> DispatchGroupAsync(
        DispatchGroup group,
        CancellationToken cancellationToken)
    {
        var results = ImmutableArray.CreateBuilder<OutboxDispatchResult<TOutbox>>(group.Items.Count);
        foreach (var item in group.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = group.Postman is null
                ? DispatchMissingPostman(item)
                : await DispatchItemAsync(group.Postman, item, cancellationToken);

            results.Add(result);
            if (result.Error is not null)
            {
                break;
            }
        }

        return results.ToImmutable();
    }

    private OutboxDispatchResult<TOutbox> DispatchMissingPostman(TOutbox item)
    {
        var itemType = getMessageType(item);
        var error = new NoPostmanRegisteredException(itemType);
        logger.LogWarning(
            error,
            "No outbox postman registered for item type {OutboxItemType} on grain {GrainType}.",
            itemType.FullName,
            grainType);
        MessagingTelemetry.RecordOutboxPostError(grainType, itemType.Name, error);
        return new OutboxDispatchResult<TOutbox>(item, error);
    }

    private async Task<OutboxDispatchResult<TOutbox>> DispatchItemAsync(
        OutboxPostmanRegistration<TOutbox> postman,
        TOutbox item,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();

        // Establishes the per-item trace context for the whole delivery. Postmen
        // that propagate Activity.Current — Orleans grain calls, and stream
        // adapters that stamp a traceparent onto the queued message — then carry
        // the trace of the request that appended this message, instead of the
        // unrelated turn that happened to trigger the drain.
        using var activity = StartDispatchActivity(item, postman);
        try
        {
            await postman.Invoke(item, cancellationToken);
            MessagingTelemetry.RecordOutboxPostItem(
                grainType,
                postman.ItemType.Name,
                success: true,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return new OutboxDispatchResult<TOutbox>(item);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var itemType = getMessageType(item);
            logger.LogError(
                ex,
                "Outbox postman failed for item type {OutboxItemType} on grain {GrainType}.",
                itemType.FullName,
                grainType);
            MessagingTelemetry.RecordOutboxPostError(grainType, itemType.Name, ex);
            MessagingTelemetry.RecordOutboxPostItem(
                grainType,
                postman.ItemType.Name,
                success: false,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return new OutboxDispatchResult<TOutbox>(item, ex);
        }
    }

    private Activity? StartDispatchActivity(TOutbox item, OutboxPostmanRegistration<TOutbox> postman)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("messaging.system", "orleans"),
            new("messaging.operation", "publish"),
            new("grain.type", grainType),
            new("event.type", getMessageType(item).Name),
            new("postman.type", postman.ItemType.Name)
        };

        // parentContext is explicitly default so the span roots its own trace, and
        // the producer context is attached as a link instead. A message can be
        // delivered hours after the appending request ended; parenting into that
        // trace would orphan the span and stretch one trace across the whole delay.
        // Same reasoning as StreamManager's consumer span.
        if (getTraceParent(item) is { } traceParent
            && ActivityContext.TryParse(traceParent, traceState: null, isRemote: true, out var producerContext))
        {
            return MessagingTelemetry.ActivitySource.StartActivity(
                "orleans.outbox.post",
                ActivityKind.Producer,
                parentContext: default,
                tags: tags,
                links: [new ActivityLink(producerContext)]);
        }

        return MessagingTelemetry.ActivitySource.StartActivity(
            "orleans.outbox.post",
            ActivityKind.Producer,
            parentContext: default,
            tags: tags);
    }

    private sealed class DispatchGroup(OutboxPostmanRegistration<TOutbox>? postman)
    {
        public OutboxPostmanRegistration<TOutbox>? Postman { get; } = postman;

        public List<TOutbox> Items { get; } = [];
    }
}
