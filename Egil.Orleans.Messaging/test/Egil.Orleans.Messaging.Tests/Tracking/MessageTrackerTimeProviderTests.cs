using Orleans.Providers.Streams.Common;
using TimeProviderExtensions;

namespace Egil.Orleans.Messaging.Tests.Tracking;

[CollectionDefinition(nameof(MessageTrackerClockCollection), DisableParallelization = true)]
public sealed class MessageTrackerClockCollection;

// These tests install the process-wide fallback clock, so they must not run
// alongside tests whose trackers rely on the system clock.
[Collection(nameof(MessageTrackerClockCollection))]
public sealed class MessageTrackerTimeProviderTests
{
    private static readonly DateTimeOffset SiloNow = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Silo_clock_stamps_trackers_without_their_own_clock()
    {
        var silo = StartSilo(UsePricingClock);

        await silo.StartAsync();
        var received = ReceivedAt(new MessageTracker());
        await silo.StopAsync();

        Assert.Equal(SiloNow, received);
    }

    [Fact]
    public async Task Silo_clock_defaults_to_the_registered_time_provider()
    {
        var builder = new FakeSiloBuilder();
        builder.Services.AddSingleton<TimeProvider>(new ManualTimeProvider(SiloNow));
        builder.ConfigureMessageTracker(static _ => { });
        var silo = new FakeSilo(builder.Services.BuildServiceProvider());

        await silo.StartAsync();
        var received = ReceivedAt(new MessageTracker());
        await silo.StopAsync();

        Assert.Equal(SiloNow, received);
    }

    [Fact]
    public async Task Tracker_clock_takes_precedence_over_silo_clock()
    {
        var ownNow = SiloNow.AddHours(1);
        var silo = StartSilo(UsePricingClock);
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(new ManualTimeProvider(ownNow));

        await silo.StartAsync();
        var received = ReceivedAt(tracker);
        await silo.StopAsync();

        Assert.Equal(ownNow, received);
    }

    [Fact]
    public async Task Silos_stopping_out_of_order_leave_the_running_silo_clock_installed()
    {
        var clockB = SiloNow.AddHours(2);
        var siloA = StartSilo(UsePricingClock);
        var siloB = StartSilo((options, _) => options.TimeProvider = new ManualTimeProvider(clockB));

        await siloA.StartAsync();
        await siloB.StartAsync();
        await siloA.StopAsync();
        var whileBRuns = ReceivedAt(new MessageTracker());
        await siloB.StopAsync();
        var afterBoth = ReceivedAt(new MessageTracker());

        Assert.Equal(clockB, whileBRuns);
        Assert.NotEqual(clockB, afterBoth);
        Assert.NotEqual(SiloNow, afterBoth);
    }

    [Fact]
    public async Task Silo_stop_removes_its_clock()
    {
        var silo = StartSilo(UsePricingClock);

        await silo.StartAsync();
        await silo.StopAsync();
        var received = ReceivedAt(new MessageTracker());

        Assert.NotEqual(SiloNow, received);
    }

    [Fact]
    public async Task Repeated_configuration_applies_in_order_with_one_installer()
    {
        var replacementNow = SiloNow.AddDays(1);
        var silo = StartSilo(
            UsePricingClock,
            (options, _) => options.TimeProvider = new ManualTimeProvider(replacementNow));

        await silo.StartAsync();
        var received = ReceivedAt(new MessageTracker());
        await silo.StopAsync();

        Assert.Equal(replacementNow, received);
        Assert.Single(silo.Participants);
    }

    private static void UsePricingClock(MessageTrackerOptions options, IServiceProvider services) =>
        options.TimeProvider = services.GetRequiredKeyedService<TimeProvider>("pricing");

    [Fact]
    public async Task Global_mode_and_retention_apply_unless_a_grain_tracker_overrides_them()
    {
        var clock = new ManualTimeProvider(SiloNow);
        var silo = StartSilo((options, _) =>
        {
            options.StreamTrackingMode = StreamTrackingMode.OutboxIdentity;
            options.RetentionPeriod = TimeSpan.FromHours(1);
            options.TimeProvider = clock;
        });
        var delivery = new StreamCursor("orders", new EventSequenceToken(1), "events")
        {
            StreamId = StreamId.Create("orders", "one"),
            OutboxToken = new(1, GrainId.Create("sender", "one"), SiloNow, SiloNow)
        };
        await silo.StartAsync();
        var inherited = new MessageTracker();
        var overridden = new MessageTracker();
        overridden.Configure(options =>
        {
            options.StreamTrackingMode = StreamTrackingMode.StreamPosition;
            options.RetentionPeriod = null;
        });
        inherited.TryAcceptMessage(delivery, out inherited);
        overridden.TryAcceptMessage(delivery, out overridden);
        var retry = delivery with { Token = new EventSequenceToken(2) };

        var globalRejectsRetry = !inherited.TryAcceptMessage(retry, out _);
        var grainAcceptsRetry = overridden.TryAcceptMessage(retry, out _);
        clock.Advance(TimeSpan.FromHours(1));
        var globalExpires = inherited.TryAcceptMessage(delivery, out _);
        var grainRetains = !overridden.TryAcceptMessage(delivery, out _);
        await silo.StopAsync();
        var afterStopUsesPosition = inherited.TryAcceptMessage(retry, out _);

        Assert.True(globalRejectsRetry);
        Assert.True(grainAcceptsRetry);
        Assert.True(globalExpires);
        Assert.True(grainRetains);
        Assert.True(afterStopUsesPosition);
    }

    private static DateTimeOffset ReceivedAt(MessageTracker tracker)
    {
        tracker.TryAcceptMessage(new StreamCursor("orders", new EventSequenceToken(1)), out var next);
        return Assert.Single(next.StreamEntries).Value.Received;
    }

    private static FakeSilo StartSilo(params Action<MessageTrackerOptions, IServiceProvider>[] registrations)
    {
        var builder = new FakeSiloBuilder();
        builder.Services.AddKeyedSingleton<TimeProvider>("pricing", new ManualTimeProvider(SiloNow));
        foreach (var registration in registrations)
        {
            builder.ConfigureMessageTracker(registration);
        }

        return new FakeSilo(builder.Services.BuildServiceProvider());
    }

    private sealed class FakeSilo : ISiloLifecycle
    {
        private readonly List<ILifecycleObserver> observers = [];

        public FakeSilo(IServiceProvider services)
        {
            Participants = services.GetServices<ILifecycleParticipant<ISiloLifecycle>>().ToList();
            foreach (var participant in Participants)
            {
                participant.Participate(this);
            }
        }

        public IReadOnlyList<ILifecycleParticipant<ISiloLifecycle>> Participants { get; }

        public int HighestCompletedStage => 0;

        public int LowestStoppedStage => 0;

        public IDisposable Subscribe(string observerName, int stage, ILifecycleObserver observer)
        {
            observers.Add(observer);
            return new NoopDisposable();
        }

        public Task StartAsync() => Task.WhenAll(observers.Select(observer => observer.OnStart(CancellationToken.None)));

        public Task StopAsync() => Task.WhenAll(observers.Select(observer => observer.OnStop(CancellationToken.None)));

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class FakeSiloBuilder : ISiloBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();

        public Microsoft.Extensions.Configuration.IConfiguration Configuration { get; } =
            new Microsoft.Extensions.Configuration.ConfigurationManager();
    }
}
