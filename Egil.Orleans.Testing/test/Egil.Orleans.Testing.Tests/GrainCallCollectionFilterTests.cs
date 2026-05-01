using System.Reflection;
using Orleans.Serialization.Invocation;

namespace Egil.Orleans.Testing.Tests;

public class GrainCallCollectionFilterTests
{
    private static readonly Type GrainCallCollectionFilterType = typeof(GrainActivityCollector).Assembly.GetType("Egil.Orleans.Testing.GrainCallCollectionFilter", throwOnError: true)!;

    [Fact]
    public async Task Invoke_throws_for_null_context()
    {
        var filter = CreateFilter(new GrainActivityCollector());

        await Assert.ThrowsAsync<ArgumentNullException>(() => filter.Invoke(null!));
    }

    [Fact]
    public async Task Invoke_observes_non_system_call_after_successful_invoke()
    {
        var collector = new GrainActivityCollector();
        var filter = CreateFilter(collector);
        var context = new TestIncomingGrainCallContext("MyApp.IMyGrain", "HandleAsync");
        var waitTask = collector.WaitForGrainCallAsync(
            candidate => ReferenceEquals(candidate, context),
            timeout: TimeSpan.FromMilliseconds(250),
            ct: TestContext.Current.CancellationToken);

        await filter.Invoke(context);
        await waitTask;

        Assert.Equal(1, context.InvokeCount);
    }

    [Fact]
    public async Task Invoke_observes_non_system_call_before_rethrowing_failure()
    {
        var collector = new GrainActivityCollector();
        var filter = CreateFilter(collector);
        var context = new TestIncomingGrainCallContext(
            "MyApp.IMyGrain",
            "HandleAsync",
            static () => throw new InvalidOperationException("boom"));
        var waitTask = collector.WaitForGrainCallAsync(
            candidate => ReferenceEquals(candidate, context),
            timeout: TimeSpan.FromMilliseconds(250),
            ct: TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => filter.Invoke(context));

        Assert.Equal("boom", exception.Message);
        await waitTask;
    }

    [Fact]
    public async Task Invoke_ignores_system_calls()
    {
        var collector = new GrainActivityCollector();
        var filter = CreateFilter(collector);
        var context = new TestIncomingGrainCallContext("Orleans.Runtime.IRemindable", "ReceiveReminder");
        var waitTask = collector.WaitForGrainCallAsync(
            _ => true,
            timeout: TimeSpan.FromMilliseconds(100),
            ct: TestContext.Current.CancellationToken);

        await filter.Invoke(context);

        await Assert.ThrowsAsync<WaitForAssertionTimeoutException>(() => waitTask);
    }

    [Fact]
    public async Task Invoke_ignores_assertion_scope_calls()
    {
        var collector = new GrainActivityCollector();
        var filter = CreateFilter(collector);
        var context = new TestIncomingGrainCallContext("MyApp.IMyGrain", "HandleAsync");
        var waitTask = collector.WaitForGrainCallAsync(
            _ => true,
            timeout: TimeSpan.FromMilliseconds(100),
            ct: TestContext.Current.CancellationToken);

        using (RequestContextScope.ForAssertion())
        {
            await filter.Invoke(context);
        }

        await Assert.ThrowsAsync<WaitForAssertionTimeoutException>(() => waitTask);
    }

    private static IIncomingGrainCallFilter CreateFilter(GrainActivityCollector collector)
        => (IIncomingGrainCallFilter)Activator.CreateInstance(GrainCallCollectionFilterType, collector)!;

    private sealed class TestIncomingGrainCallContext(
        string interfaceTypeText,
        string methodName,
        Func<Task>? invoke = null)
        : IIncomingGrainCallContext
    {
        private readonly Func<Task> invoke = invoke ?? (() => Task.CompletedTask);

        public int InvokeCount { get; private set; }

        public object Grain => new();

        public MethodInfo ImplementationMethod => typeof(TestIncomingGrainCallContext).GetMethod(nameof(Invoke))!;

        public MethodInfo InterfaceMethod => typeof(TestIncomingGrainCallContext).GetMethod(nameof(Invoke))!;

        public string InterfaceName => interfaceTypeText;

        public GrainInterfaceType InterfaceType { get; } = GrainInterfaceType.Create(interfaceTypeText);

        public string MethodName => methodName;

        public IInvokable Request => null!;

        public object? Result { get; set; }

#pragma warning disable CS8769
        Response IGrainCallContext.Response { get; set; } = null!;
#pragma warning restore CS8769

        public GrainId? SourceId => null;

        public GrainId TargetId { get; } = GrainId.Create("test-grain", Guid.NewGuid().ToString("N"));

        public IGrainContext TargetContext => null!;

        public async Task Invoke()
        {
            InvokeCount++;
            await invoke();
        }
    }
}
