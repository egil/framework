using Microsoft.Extensions.Options;
using Orleans.Providers.Streams.Common;
using TimeProviderExtensions;

namespace Egil.Orleans.Messaging.Tests.Streams;

public sealed class StreamManagerServiceCollectionExtensionsTests
{
    [Fact]
    public void Silo_defaults_share_a_keyed_clock_from_services()
    {
        var pricingClock = new ManualTimeProvider();
        var builder = new FakeSiloBuilder();
        builder.Services.AddKeyedSingleton<TimeProvider>("pricing", pricingClock);

        builder.ConfigureStreamManager((options, services) =>
            options.TimeProvider = services.GetRequiredKeyedService<TimeProvider>("pricing"));

        var defaults = CreateDefaults(builder);
        Assert.Same(pricingClock, defaults.TimeProvider);
    }

    [Fact]
    public void Repeated_silo_configuration_applies_every_step()
    {
        var builder = new FakeSiloBuilder();

        builder
            .ConfigureStreamManager(options => options.Trace = MessageTraceOptions.Parent)
            .ConfigureStreamManager(options => options.UseTrackedResumeToken = false);

        var defaults = CreateDefaults(builder);
        Assert.Equal(MessageTraceOptions.Parent, defaults.Trace);
        Assert.False(defaults.UseTrackedResumeToken);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, 7L)]
    public async Task Subscription_starts_from_silo_defaults_and_can_override_them(bool overrideDefault, long? expectedSequence)
    {
        var builder = new FakeSiloBuilder();
        builder.ConfigureStreamManager(options => options.UseTrackedResumeToken = false);
        var factory = builder.Services.BuildServiceProvider().GetRequiredService<IOptionsFactory<StreamSubscriptionOptions>>();
        var tracker = new MessageTracker();
        tracker.TryAcceptMessage(new StreamCursor("orders", new EventSequenceToken(7), "provider-a"), out tracker);
        var stream = new StreamManagerResumeTests.FakeStream<string>("provider-a", StreamId.Create("orders", "one"));
        var manager = StreamManagerResumeTests.CreateManager(
            () => tracker, stream, createDefaultOptions: () => factory.Create(Options.DefaultName));

        await manager
            .ConfigureExplicitSubscription<string>(
                "provider-a",
                "orders",
                static (_, _) => ValueTask.CompletedTask,
                overrideDefault ? options => options.UseTrackedResumeToken = true : null)
            .EnsureExplicitSubscriptionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expectedSequence, (stream.SubscribeToken as EventSequenceToken)?.SequenceNumber);
    }

    [Fact]
    public void Subscription_changes_do_not_leak_into_silo_defaults()
    {
        var builder = new FakeSiloBuilder();
        builder.ConfigureStreamManager(options => options.Trace = MessageTraceOptions.Parent);
        var factory = builder.Services.BuildServiceProvider().GetRequiredService<IOptionsFactory<StreamSubscriptionOptions>>();
        var stream = new StreamManagerResumeTests.FakeStream<string>("provider-a", StreamId.Create("orders", "one"));
        var manager = StreamManagerResumeTests.CreateManager(
            null, stream, createDefaultOptions: () => factory.Create(Options.DefaultName));
        StreamSubscriptionOptions? first = null;
        StreamSubscriptionOptions? second = null;

        manager
            .ConfigureExplicitSubscription<string>("provider-a", "orders", static (_, _) => ValueTask.CompletedTask, options =>
            {
                first = options;
                options.Trace = MessageTraceOptions.Link;
            })
            .ConfigureExplicitSubscription<string>("provider-a", "other", static (_, _) => ValueTask.CompletedTask, options => second = options);

        Assert.NotSame(first, second);
        Assert.Equal(MessageTraceOptions.Parent, second!.Trace);
    }

    [Fact]
    public void Subscription_rejects_null_trace()
    {
        var stream = new StreamManagerResumeTests.FakeStream<string>("provider-a", StreamId.Create("orders", "one"));
        var manager = StreamManagerResumeTests.CreateManager(null, stream);

        Assert.Throws<InvalidOperationException>(() => manager.ConfigureExplicitSubscription<string>(
            "provider-a", "orders", static (_, _) => ValueTask.CompletedTask, options => options.Trace = null!));
    }

    private static StreamSubscriptionOptions CreateDefaults(FakeSiloBuilder builder) =>
        builder.Services.BuildServiceProvider()
            .GetRequiredService<IOptionsFactory<StreamSubscriptionOptions>>()
            .Create(Options.DefaultName);

    private sealed class FakeSiloBuilder : ISiloBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();

        public Microsoft.Extensions.Configuration.IConfiguration Configuration { get; } =
            new Microsoft.Extensions.Configuration.ConfigurationManager();
    }
}
