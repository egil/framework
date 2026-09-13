using System.Collections.Concurrent;
using System.Diagnostics;
using Orleans.Providers.Streams.Common;

namespace Egil.Orleans.Messaging.Tests.Streams;

public sealed class StreamManagerBehaviorTests(MessagingTestClusterFixture fixture) : IClassFixture<MessagingTestClusterFixture>
{
    [Theory]
    [InlineData(SubscriptionKind.Implicit)]
    [InlineData(SubscriptionKind.Convention)]
    [InlineData(SubscriptionKind.ExplicitId)]
    public async Task Rejected_duplicate_keeps_original_subscription_handler(SubscriptionKind subscriptionKind)
    {
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerBehaviorGrain>(Guid.NewGuid());

        var result = await grain.DuplicateAsync(subscriptionKind);

        Assert.True(result.Rejected);
        Assert.Equal(["original"], result.Delivered);
    }

    [Theory]
    [InlineData("00-11111111111111111111111111111111-2222222222222222-01", true)]
    [InlineData("invalid-traceparent", false)]
    public async Task Consumer_tracing_links_valid_context_and_records_handler_failure(string traceParent, bool expectedLink)
    {
        var streamNamespace = Guid.NewGuid().ToString("N");
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "egil.orleans.messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (Equals(activity.GetTagItem("messaging.destination.name"), streamNamespace))
                {
                    activities.Enqueue(activity);
                }
            }
        };
        ActivitySource.AddActivityListener(listener);
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerBehaviorGrain>(Guid.NewGuid());

        var result = await grain.DeliverAsync(streamNamespace, false, traceParent);

        Assert.Equal(["after-failure"], result.Delivered);
        Assert.Equal(2, activities.Count);
        var failed = Assert.Single(activities, activity => activity.Status == ActivityStatusCode.Error);
        Assert.Equal("Delivery failed.", failed.StatusDescription);
        Assert.All(activities, activity =>
        {
            Assert.Equal(ActivityKind.Consumer, activity.Kind);
            Assert.Equal(expectedLink ? 1 : 0, activity.Links.Count());
        });
        if (expectedLink)
        {
            var link = Assert.Single(failed.Links);
            Assert.Equal("11111111111111111111111111111111", link.Context.TraceId.ToString());
            Assert.Equal("2222222222222222", link.Context.SpanId.ToString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Custom_error_callback_does_not_prevent_subsequent_delivery(bool callbackThrows)
    {
        var grain = fixture.GrainFactory.GetGrain<IStreamManagerBehaviorGrain>(Guid.NewGuid());
        var streamNamespace = Guid.NewGuid().ToString("N");

        var result = await grain.DeliverAsync(streamNamespace, callbackThrows, null);

        Assert.Equal(["after-failure"], result.Delivered);
        Assert.Equal(1, result.Errors);
        Assert.True(result.OriginalError);
        Assert.Equal(streamNamespace, result.ErrorNamespace);
    }
}

public interface IStreamManagerBehaviorGrain : IGrainWithGuidKey
{
    Task<(bool Rejected, string[] Delivered)> DuplicateAsync(SubscriptionKind subscriptionKind);
    Task<(string[] Delivered, int Errors, bool OriginalError, string? ErrorNamespace)> DeliverAsync(string streamNamespace, bool callbackThrows, string? traceParent);
}

public sealed class StreamManagerBehaviorGrain : Grain, IStreamManagerBehaviorGrain
{
    public async Task<(bool Rejected, string[] Delivered)> DuplicateAsync(SubscriptionKind subscriptionKind)
    {
        var streamId = StreamId.Create("orders", "one");
        var stream = new StreamManagerResumeTests.FakeStream<string>("provider-a", streamId);
        var manager = StreamManagerResumeTests.CreateManager(null, stream, this);
        List<string> delivered = [];
        void Configure(Func<string, StreamCursor, ValueTask> handler)
        {
            switch (subscriptionKind)
            {
                case SubscriptionKind.Implicit:
                    manager.ConfigureImplicitSubscription("orders", handler);
                    break;
                case SubscriptionKind.Convention:
                    manager.ConfigureExplicitSubscription("provider-a", "orders", handler);
                    break;
                default:
                    manager.ConfigureExplicitSubscription("provider-a", streamId, handler);
                    break;
            }
        }
        Configure((message, _) =>
        {
            delivered.Add(message);
            return ValueTask.CompletedTask;
        });
        var rejected = false;
        try
        {
            Configure((_, _) => throw new InvalidOperationException("Replacement handler must not run."));
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        if (subscriptionKind == SubscriptionKind.Implicit)
        {
            var handle = new StreamManagerResumeTests.FakeSubscriptionHandle<string>("provider-a", streamId);
            await ((IStreamManagerComponent)manager).OnSubscribedAsync(
                new StreamManagerResumeTests.FakeStreamSubscriptionHandleFactory("provider-a", streamId, handle));
            await handle.DeliverAsync("original");
        }
        else
        {
            await manager.EnsureExplicitSubscriptionsAsync();
            await stream.OnNextAsync("original");
        }
        return (rejected, delivered.ToArray());
    }

    public async Task<(string[] Delivered, int Errors, bool OriginalError, string? ErrorNamespace)> DeliverAsync(
        string streamNamespace, bool callbackThrows, string? traceParent)
    {
        var stream = new StreamManagerResumeTests.FakeStream<string>("provider-a", StreamId.Create(streamNamespace, "one"));
        var manager = StreamManagerResumeTests.CreateManager(null, stream, this);
        List<string> delivered = [];
        var failure = new InvalidOperationException("Delivery failed.");
        var errors = 0;
        var originalError = false;
        string? errorNamespace = null;
        manager.ConfigureExplicitSubscription<string>("provider-a", streamNamespace,
            (message, _) =>
            {
                if (message == "fail")
                {
                    throw failure;
                }
                delivered.Add(message);
                return ValueTask.CompletedTask;
            },
            (name, error) =>
            {
                errors++;
                errorNamespace = name;
                originalError = ReferenceEquals(error, failure);
                if (callbackThrows)
                {
                    throw new InvalidOperationException("Error callback failed.");
                }
            });
        await manager.EnsureExplicitSubscriptionsAsync();
        await stream.OnNextAsync("fail", new DiagnosticToken(traceParent));
        await stream.OnNextAsync("after-failure", new DiagnosticToken(traceParent));
        return (delivered.ToArray(), errors, originalError, errorNamespace);
    }
}

internal sealed class DiagnosticToken(string? traceParent, DateTimeOffset? enqueuedTime = null) : EventSequenceToken(1), IStreamSequenceTokenMetadata
{
    public bool TryGetTraceParent([NotNullWhen(true)] out string? value)
    {
        value = traceParent;
        return value is not null;
    }

    public bool TryGetEnqueuedTime(out DateTimeOffset value)
    {
        value = enqueuedTime.GetValueOrDefault();
        return enqueuedTime.HasValue;
    }

    public bool TryGetProviderName([NotNullWhen(true)] out string? value)
    {
        value = "provider-a";
        return true;
    }
}

public enum SubscriptionKind
{
    Implicit,
    Convention,
    ExplicitId
}
