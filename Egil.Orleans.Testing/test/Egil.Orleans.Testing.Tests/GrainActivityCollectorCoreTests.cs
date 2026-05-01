using System.Diagnostics;
using System.Threading.Channels;
using System.Reflection;
using Orleans.Serialization.Invocation;

namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorCoreTests(OrleansTestClusterFixture fixture)
{
    private static readonly int SubscriberChannelCapacity =
        (int)typeof(GrainActivityCollector).GetField("SubscriberChannelCapacity", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;

    private static readonly MethodInfo OnStorageOperationMethod =
        typeof(GrainActivityCollector).GetMethod("OnStorageOperation", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo OnGrainCallMethod =
        typeof(GrainActivityCollector).GetMethod("OnGrainCall", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo SubscribeActivitiesMethod =
        typeof(GrainActivityCollector).GetMethod("SubscribeActivities", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo SubscribeStorageOperationsMethod =
        typeof(GrainActivityCollector).GetMethod("SubscribeStorageOperations", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo SubscribeGrainCallsMethod =
        typeof(GrainActivityCollector).GetMethod("SubscribeGrainCalls", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo ActivitySubscribersField =
        typeof(GrainActivityCollector).GetField("activitySubscribers", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo StorageSubscribersField =
        typeof(GrainActivityCollector).GetField("storageSubscribers", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public async Task WaitForAssertionAsync_with_grain_parameter_task_throws_for_null_grain()
    {
        var collector = new GrainActivityCollector();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            collector.WaitForAssertionAsync<ITestStateGrain>(
                null!,
                static _ => Task.CompletedTask,
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForAssertionAsync_with_grain_parameter_task_result_throws_for_null_grain()
    {
        var collector = new GrainActivityCollector();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            collector.WaitForAssertionAsync<ITestStateGrain, int>(
                null!,
                static _ => Task.FromResult(1),
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForStorageOperationAsync_with_grain_scope_throws_for_null_grain()
    {
        var collector = new GrainActivityCollector();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            collector.WaitForStorageOperationAsync<ITestStateGrain>(
                null!,
                static _ => true,
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForGrainCallAsync_with_grain_scope_throws_for_null_grain()
    {
        var collector = new GrainActivityCollector();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            collector.WaitForGrainCallAsync<ITestStateGrain>(
                null!,
                static _ => true,
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForStorageOperationAsync_with_grain_scope_filters_out_other_grains()
    {
        var collector = new GrainActivityCollector();
        var expectedGrain = fixture.GetUniqueGrain<ITestStateGrain>();
        var otherGrain = fixture.GetUniqueGrain<ITestStateGrain>();
        var sawUnexpectedOperation = false;
        var waitTask = collector.WaitForStorageOperationAsync(
            expectedGrain,
            operation =>
            {
                sawUnexpectedOperation |= operation.GrainId != expectedGrain.GetGrainId();
                return true;
            },
            timeout: TimeSpan.FromMilliseconds(250),
            ct: TestContext.Current.CancellationToken);

        InvokeOnStorageOperation(
            collector,
            new StorageOperation(StorageOperationKind.Write, otherGrain.GetGrainId(), "Default", "state", "etag-1", 1));
        await Task.Yield();

        Assert.False(sawUnexpectedOperation);

        InvokeOnStorageOperation(
            collector,
            new StorageOperation(StorageOperationKind.Write, expectedGrain.GetGrainId(), "Default", "state", "etag-2", 2));

        await waitTask;
    }

    [Fact]
    public async Task WaitForGrainCallAsync_with_grain_scope_filters_out_other_grains()
    {
        var collector = new GrainActivityCollector();
        var expectedGrain = fixture.GetUniqueGrain<ITestStateGrain>();
        var otherGrain = fixture.GetUniqueGrain<ITestStateGrain>();
        var sawUnexpectedCall = false;
        var waitTask = collector.WaitForGrainCallAsync(
            expectedGrain,
            context =>
            {
                sawUnexpectedCall |= context.TargetId != expectedGrain.GetGrainId();
                return true;
            },
            timeout: TimeSpan.FromMilliseconds(250),
            ct: TestContext.Current.CancellationToken);

        InvokeOnGrainCall(collector, new TestIncomingGrainCallContext(otherGrain.GetGrainId()));
        await Task.Yield();

        Assert.False(sawUnexpectedCall);

        InvokeOnGrainCall(collector, new TestIncomingGrainCallContext(expectedGrain.GetGrainId()));

        await waitTask;
    }

    [Fact]
    public async Task OnStorageOperation_retries_standard_waits()
    {
        var collector = new GrainActivityCollector();
        var ready = false;
        var waitTask = collector.WaitForAssertionAsync(
            () =>
            {
                Assert.True(ready);
                return Task.CompletedTask;
            },
            timeout: TimeSpan.FromMilliseconds(250),
            ct: TestContext.Current.CancellationToken);

        ready = true;
        InvokeOnStorageOperation(
            collector,
            new StorageOperation(StorageOperationKind.Write, GrainId.Create("test-grain", "storage"), "Default", "state", "etag", 1));

        await waitTask;
    }

    [Fact]
    public async Task OnStorageOperation_clear_retries_standard_waits()
    {
        var collector = new GrainActivityCollector();
        var ready = false;
        var waitTask = collector.WaitForAssertionAsync(
            () =>
            {
                Assert.True(ready);
                return Task.CompletedTask;
            },
            timeout: TimeSpan.FromMilliseconds(250),
            ct: TestContext.Current.CancellationToken);

        ready = true;
        InvokeOnStorageOperation(
            collector,
            new StorageOperation(StorageOperationKind.Clear, GrainId.Create("test-grain", "clear"), "Default", "state", "etag", 1));

        await waitTask;
    }

    [Fact]
    public async Task OnGrainCall_retries_standard_waits()
    {
        var collector = new GrainActivityCollector();
        var ready = false;
        var waitTask = collector.WaitForAssertionAsync(
            () =>
            {
                Assert.True(ready);
                return Task.CompletedTask;
            },
            timeout: TimeSpan.FromMilliseconds(250),
            ct: TestContext.Current.CancellationToken);

        ready = true;
        InvokeOnGrainCall(collector, new TestIncomingGrainCallContext(GrainId.Create("test-grain", "call")));

        await waitTask;
    }

    [Fact]
    public async Task WaitForStorageOperationAsync_throws_for_null_predicate()
    {
        var collector = new GrainActivityCollector();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            collector.WaitForStorageOperationAsync(
                (Func<StorageOperation, bool>)null!,
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForGrainCallAsync_throws_for_null_predicate()
    {
        var collector = new GrainActivityCollector();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            collector.WaitForGrainCallAsync(
                (Func<IIncomingGrainCallContext, bool>)null!,
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OnStorageOperation_publishes_to_WaitForStorageOperationAsync()
    {
        var collector = new GrainActivityCollector();
        var grainId = GrainId.Create("test-grain", "storage-direct");
        var operation = new StorageOperation(StorageOperationKind.Write, grainId, "Default", "state", "etag", 99);
        var waitTask = collector.WaitForStorageOperationAsync(
            op => ReferenceEquals(op, operation),
            timeout: TimeSpan.FromMilliseconds(250),
            ct: TestContext.Current.CancellationToken);

        InvokeOnStorageOperation(collector, operation);

        await waitTask;
    }

    [Fact]
    public void OnStorageOperation_throws_when_activity_subscriber_channel_is_full()
    {
        var collector = new GrainActivityCollector();
        using var subscription = SubscribeActivities(collector, out _);
        var operation = new StorageOperation(StorageOperationKind.Write, GrainId.Create("test-grain", "activity-full"), "Default", "state", "etag", 1);

        FillToCapacity(() => InvokeOnStorageOperation(collector, operation));

        var exception = Assert.Throws<TargetInvocationException>(() => InvokeOnStorageOperation(collector, operation));

        Assert.Equal("A grain activity subscriber channel is full.", exception.InnerException?.Message);
    }

    [Fact]
    public void OnStorageOperation_throws_when_storage_subscriber_channel_is_full()
    {
        var collector = new GrainActivityCollector();
        using var subscription = SubscribeStorageOperations(collector, out _);
        var operation = new StorageOperation(StorageOperationKind.Write, GrainId.Create("test-grain", "storage-full"), "Default", "state", "etag", 1);

        FillToCapacity(() => InvokeOnStorageOperation(collector, operation));

        var exception = Assert.Throws<TargetInvocationException>(() => InvokeOnStorageOperation(collector, operation));

        Assert.Equal("A storage operation subscriber channel is full.", exception.InnerException?.Message);
    }

    [Fact]
    public async Task OnGrainCall_publishes_to_WaitForGrainCallAsync()
    {
        var collector = new GrainActivityCollector();
        var context = new TestIncomingGrainCallContext(GrainId.Create("test-grain", "call-direct"));
        var waitTask = collector.WaitForGrainCallAsync(
            ctx => ReferenceEquals(ctx, context),
            timeout: TimeSpan.FromMilliseconds(250),
            ct: TestContext.Current.CancellationToken);

        InvokeOnGrainCall(collector, context);

        await waitTask;
    }

    [Fact]
    public async Task WaitForAssertionAsync_throws_when_activity_stream_completes_unexpectedly()
    {
        var collector = new GrainActivityCollector();
        var waitTask = collector.WaitForAssertionAsync(
            static () => Task.FromException(new Xunit.Sdk.XunitException("Keep waiting.")),
            timeout: TimeSpan.FromSeconds(5),
            ct: TestContext.Current.CancellationToken);

        UnsubscribeFirstSubscriber(collector, ActivitySubscribersField);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => waitTask);

        Assert.Equal("The activity stream completed unexpectedly.", exception.Message);
    }

    [Fact]
    public async Task WaitForStorageOperationAsync_throws_when_event_stream_completes_unexpectedly()
    {
        var collector = new GrainActivityCollector();
        var waitTask = collector.WaitForStorageOperationAsync(
            static _ => false,
            timeout: TimeSpan.FromSeconds(5),
            ct: TestContext.Current.CancellationToken);

        UnsubscribeFirstSubscriber(collector, StorageSubscribersField);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => waitTask);

        Assert.Equal("The event stream completed unexpectedly.", exception.Message);
    }

    [Fact]
    public void OnGrainCall_throws_when_grain_call_subscriber_channel_is_full()
    {
        var collector = new GrainActivityCollector();
        using var subscription = SubscribeGrainCalls(collector, out _);
        var context = new TestIncomingGrainCallContext(GrainId.Create("test-grain", "grain-call-full"));

        FillToCapacity(() => InvokeOnGrainCall(collector, context));

        var exception = Assert.Throws<TargetInvocationException>(() => InvokeOnGrainCall(collector, context));

        Assert.Equal("A grain call subscriber channel is full.", exception.InnerException?.Message);
    }

    [Fact]
    public void OnStorageOperation_throws_for_null_operation()
    {
        var collector = new GrainActivityCollector();

        var exception = Assert.Throws<TargetInvocationException>(() => OnStorageOperationMethod.Invoke(collector, [null]));

        Assert.IsType<ArgumentNullException>(exception.InnerException);
    }

    [Fact]
    public void OnStorageOperation_throws_for_unknown_operation_kind()
    {
        var collector = new GrainActivityCollector();
        var operation = new StorageOperation((StorageOperationKind)999, GrainId.Create("test-grain", "unknown-kind"), "Default", "state", "etag", 1);

        var exception = Assert.Throws<TargetInvocationException>(() => InvokeOnStorageOperation(collector, operation));

        Assert.IsType<UnreachableException>(exception.InnerException);
    }

    [Fact]
    public void OnGrainCall_throws_for_null_context()
    {
        var collector = new GrainActivityCollector();

        var exception = Assert.Throws<TargetInvocationException>(() => OnGrainCallMethod.Invoke(collector, [null]));

        Assert.IsType<ArgumentNullException>(exception.InnerException);
    }

    private static void InvokeOnStorageOperation(GrainActivityCollector collector, StorageOperation operation)
        => OnStorageOperationMethod.Invoke(collector, [operation]);

    private static void InvokeOnGrainCall(GrainActivityCollector collector, IIncomingGrainCallContext context)
        => OnGrainCallMethod.Invoke(collector, [context]);

    private static IDisposable SubscribeActivities(GrainActivityCollector collector, out ChannelReader<GrainActivity> reader)
    {
        var args = new object?[] { null, null };
        var subscription = (IDisposable)SubscribeActivitiesMethod.Invoke(collector, args)!;
        reader = (ChannelReader<GrainActivity>)args[0]!;
        return subscription;
    }

    private static IDisposable SubscribeStorageOperations(GrainActivityCollector collector, out ChannelReader<StorageOperation> reader)
    {
        var args = new object?[] { null, null };
        var subscription = (IDisposable)SubscribeStorageOperationsMethod.Invoke(collector, args)!;
        reader = (ChannelReader<StorageOperation>)args[0]!;
        return subscription;
    }

    private static IDisposable SubscribeGrainCalls(GrainActivityCollector collector, out ChannelReader<IIncomingGrainCallContext> reader)
    {
        var args = new object?[] { null, null };
        var subscription = (IDisposable)SubscribeGrainCallsMethod.Invoke(collector, args)!;
        reader = (ChannelReader<IIncomingGrainCallContext>)args[0]!;
        return subscription;
    }

    private static void FillToCapacity(Action publish)
    {
        for (var index = 0; index < SubscriberChannelCapacity; index++)
        {
            publish();
        }
    }

    private static void UnsubscribeFirstSubscriber(GrainActivityCollector collector, FieldInfo subscribersField)
    {
        var subscribers = (System.Collections.IList)subscribersField.GetValue(collector)!;
        var subscriber = Assert.Single(subscribers.Cast<object>());
        var unsubscribeMethod = typeof(GrainActivityCollector).GetMethod(
            "Unsubscribe",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [subscriber.GetType()],
            modifiers: null)!;

        unsubscribeMethod.Invoke(collector, [subscriber]);
    }

    private sealed class TestIncomingGrainCallContext(GrainId targetId) : IIncomingGrainCallContext
    {
        public object Grain => new();

        public MethodInfo ImplementationMethod => typeof(TestIncomingGrainCallContext).GetMethod(nameof(Invoke))!;

        public MethodInfo InterfaceMethod => typeof(TestIncomingGrainCallContext).GetMethod(nameof(Invoke))!;

        public string InterfaceName => "MyApp.IMyGrain";

        public GrainInterfaceType InterfaceType { get; } = GrainInterfaceType.Create("MyApp.IMyGrain");

        public string MethodName => "HandleAsync";

        public IInvokable Request => null!;

        public object? Result { get; set; }

#pragma warning disable CS8769
        Response IGrainCallContext.Response { get; set; } = null!;
#pragma warning restore CS8769

        public GrainId? SourceId => null;

        public GrainId TargetId { get; } = targetId;

        public IGrainContext TargetContext => null!;

        public Task Invoke() => Task.CompletedTask;
    }
}
