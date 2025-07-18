using Egil.Orleans.EventSourcing.Handlers;
using Egil.Orleans.EventSourcing.Tests;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Egil.Orleans.EventSourcing.Reactors;

namespace Egil.Orleans.EventSourcing;

public class EventStoreTests(SiloFixture fixture) : IClassFixture<SiloFixture>
{
    private record class Projection(string StrValue, int IntValue) : IEventProjection<Projection>
    {
        public static Projection CreateDefault()
            => new(string.Empty, 0);
    }

    [JsonDerivedType(typeof(StrEvent), "StrEvent.V1")]
    [JsonDerivedType(typeof(IntEvent), "IntEvent.V1")]
    private interface IEvent;

    private record class StrEvent(string Value) : IEvent;

    private record class IntEvent(int Value) : IEvent;

    // Test reactor for testing purposes
    private class TestStrEventReactor : IEventReactor<StrEvent, Projection>
    {
        public string Id { get; } = "TestStrEventReactor";
        public List<StrEvent> ProcessedEvents { get; } = new();

        public ValueTask ReactAsync(IEnumerable<StrEvent> events, Projection projection, IEventReactContext context, CancellationToken cancellationToken = default)
        {
            ProcessedEvents.AddRange(events);
            return ValueTask.CompletedTask;
        }
    }

    // Second test reactor for testing multiple reactors
    private class SecondTestStrEventReactor : IEventReactor<StrEvent, Projection>
    {
        public string Id { get; } = "SecondTestStrEventReactor";
        public List<StrEvent> ProcessedEvents { get; } = new();

        public ValueTask ReactAsync(IEnumerable<StrEvent> events, Projection projection, IEventReactContext context, CancellationToken cancellationToken = default)
        {
            ProcessedEvents.AddRange(events);
            return ValueTask.CompletedTask;
        }
    }

    // Failing reactor for testing failure scenarios
    private class FailingTestReactor : IEventReactor<StrEvent, Projection>
    {
        public string Id { get; } = "FailingTestReactor";
        public int CallCount { get; private set; }

        public ValueTask ReactAsync(IEnumerable<StrEvent> events, Projection projection, IEventReactContext context, CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("Reactor failed intentionally");
        }
    }

    private static GrainId RandomGrainId([CallerMemberName] string memberName = "")
        => GrainId.Create(GrainType.Create("TestEventGrain"), IdSpan.Create($"{memberName}-{Guid.NewGuid().ToString("N")[0..8]}"));

    private EventStore<Projection> CreateSut()
        => (EventStore<Projection>)fixture.Services
        .GetRequiredService<IEventStoreFactory>()
        .CreateEventStore<Projection>(fixture.Services);

    private void ConfigureEventStoreDefaultStreamConfiguration(EventStore<Projection> sut, GrainId grainId)
        => sut.Configure(
        grainId,
        new DummyGrain(),
        fixture.Services,
            builder => builder
                .AddStream<IEvent>()
                .Handle<StrEvent>((evt, pro) => pro with { StrValue = evt.Value })
                .Handle<IntEvent>((evt, pro) => pro with { IntValue = evt.Value }));

    [Fact]
    public async Task Initialize_with_empty_event_store()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        sut.Configure(grainId, new DummyGrain(), fixture.Services, b => b.AddStream<IEvent>());

        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Projection.CreateDefault(), sut.Projection);
    }

    [Fact]
    public async Task Initialize_with_unapplied_events()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        ConfigureEventStoreDefaultStreamConfiguration(sut, grainId);
        sut.AppendEvent(new StrEvent("Hello"));
        await sut.CommitAsync(TestContext.Current.CancellationToken);
        Assert.True(sut.HasUnappliedEvents);
        sut = CreateSut();
        ConfigureEventStoreDefaultStreamConfiguration(sut, grainId);

        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.False(sut.HasUnappliedEvents);
        Assert.Equal(new Projection(StrValue: "Hello", IntValue: 0), sut.Projection);
    }

    [Fact]
    public async Task Initialize_with_applied_events()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        ConfigureEventStoreDefaultStreamConfiguration(sut, grainId);
        sut.AppendEvent(new StrEvent("Hello"));
        await sut.ApplyEventsAsync(new EventHandlerContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);
        await sut.CommitAsync(TestContext.Current.CancellationToken);
        Assert.False(sut.HasUnappliedEvents);
        sut = CreateSut();
        ConfigureEventStoreDefaultStreamConfiguration(sut, grainId);

        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.False(sut.HasUnappliedEvents);
        Assert.Equal(new Projection(StrValue: "Hello", IntValue: 0), sut.Projection);
    }

    [Fact]
    public async Task Append_single_event()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        ConfigureEventStoreDefaultStreamConfiguration(sut, grainId);
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        sut.AppendEvent(new StrEvent("Hello"));

        var res = await sut.GetEventsAsync<IEvent>(default, TestContext.Current.CancellationToken).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(sut.HasUnappliedEvents);
        Assert.Equal(new StrEvent("Hello"), Assert.Single(res));
    }

    [Fact]
    public async Task Append_single_event_then_commit()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        ConfigureEventStoreDefaultStreamConfiguration(sut, grainId);
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        sut.AppendEvent(new StrEvent("Hello"));
        await sut.CommitAsync(TestContext.Current.CancellationToken);

        var res = await sut.GetEventsAsync<IEvent>(default, TestContext.Current.CancellationToken).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(sut.HasUnappliedEvents);
        Assert.Equal(new StrEvent("Hello"), Assert.Single(res));
    }

    [Fact]
    public async Task Append_and_apply_single_event()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        ConfigureEventStoreDefaultStreamConfiguration(sut, grainId);
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        sut.AppendEvent(new StrEvent("Hello"));
        await sut.ApplyEventsAsync(new EventHandlerContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);

        Assert.False(sut.HasUnappliedEvents);
        Assert.Equal(new Projection(StrValue: "Hello", IntValue: 0), sut.Projection);
    }

    [Fact]
    public async Task Commit_with_no_uncommitted_events()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        ConfigureEventStoreDefaultStreamConfiguration(sut, grainId);
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        await sut.CommitAsync(TestContext.Current.CancellationToken);

        Assert.False(sut.HasUnappliedEvents);
        Assert.Equal(Projection.CreateDefault(), sut.Projection);
    }

    [Fact]
    public async Task Append_multiple_events_of_different_types()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        ConfigureEventStoreDefaultStreamConfiguration(sut, grainId);
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        sut.AppendEvent(new StrEvent("Hello"));
        sut.AppendEvent(new IntEvent(42));
        sut.AppendEvent(new StrEvent("World"));

        var res = await sut.GetEventsAsync<IEvent>(default, TestContext.Current.CancellationToken).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(sut.HasUnappliedEvents);
        Assert.Equal(3, res.Count);
        Assert.Equal(new StrEvent("Hello"), res[0]);
        Assert.Equal(new IntEvent(42), res[1]);
        Assert.Equal(new StrEvent("World"), res[2]);
    }

    [Fact]
    public async Task Append_multiple_events_then_commit()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        ConfigureEventStoreDefaultStreamConfiguration(sut, grainId);
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        sut.AppendEvent(new StrEvent("Hello"));
        sut.AppendEvent(new IntEvent(42));
        await sut.CommitAsync(TestContext.Current.CancellationToken);

        var res = await sut.GetEventsAsync<IEvent>(default, TestContext.Current.CancellationToken).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(sut.HasUnappliedEvents);
        Assert.Equal(2, res.Count);
        Assert.Equal(new StrEvent("Hello"), res[0]);
        Assert.Equal(new IntEvent(42), res[1]);
    }

    [Fact]
    public async Task Append_multiple_events_then_apply()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        ConfigureEventStoreDefaultStreamConfiguration(sut, grainId);
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        sut.AppendEvent(new StrEvent("Hello"));
        sut.AppendEvent(new IntEvent(42));
        sut.AppendEvent(new StrEvent("World"));
        await sut.ApplyEventsAsync(new EventHandlerContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);

        Assert.False(sut.HasUnappliedEvents);
        Assert.Equal(new Projection(StrValue: "World", IntValue: 42), sut.Projection);
    }

    [Fact]
    public async Task Append_apply_and_commit_multiple_events()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        ConfigureEventStoreDefaultStreamConfiguration(sut, grainId);
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        sut.AppendEvent(new StrEvent("Hello"));
        sut.AppendEvent(new IntEvent(42));
        await sut.ApplyEventsAsync(new EventHandlerContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);
        await sut.CommitAsync(TestContext.Current.CancellationToken);

        Assert.False(sut.HasUnappliedEvents);
        Assert.Equal(new Projection(StrValue: "Hello", IntValue: 42), sut.Projection);
        var res = await sut.GetEventsAsync<IEvent>(default, TestContext.Current.CancellationToken).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, res.Count);
    }

    [Fact]
    public async Task GetEventsAsync_with_specific_event_type()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        ConfigureEventStoreDefaultStreamConfiguration(sut, grainId);
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        sut.AppendEvent(new StrEvent("Hello"));
        sut.AppendEvent(new IntEvent(42));
        sut.AppendEvent(new StrEvent("World"));

        var strEvents = await sut.GetEventsAsync<StrEvent>(default, TestContext.Current.CancellationToken).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        var intEvents = await sut.GetEventsAsync<IntEvent>(default, TestContext.Current.CancellationToken).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, strEvents.Count);
        Assert.Equal(new StrEvent("Hello"), strEvents[0]);
        Assert.Equal(new StrEvent("World"), strEvents[1]);
        Assert.Single(intEvents);
        Assert.Equal(new IntEvent(42), intEvents[0]);
    }

    [Fact]
    public async Task AppendEvent_throws_when_no_matching_stream()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        sut.Configure(grainId, new DummyGrain(), fixture.Services, b => b.AddStream<StrEvent>());
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        var exception = Assert.Throws<InvalidOperationException>(() => sut.AppendEvent(new IntEvent(42)));
        Assert.Contains("No event stream found for event type", exception.Message);
        Assert.Contains(typeof(IntEvent).FullName ?? "", exception.Message);
    }

    private record class UnknownEvent(string Value);

    [Fact]
    public async Task AppendEvent_throws_when_multiple_matching_streams()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        sut.Configure(
            grainId,
            new DummyGrain(),
            fixture.Services,
            builder =>
            {
                builder.AddStream<IEvent>("Stream1");
                builder.AddStream<IEvent>("Stream2");
            });
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        var exception = Assert.Throws<InvalidOperationException>(() => sut.AppendEvent(new StrEvent("Hello")));
        Assert.Contains("matches multiple streams", exception.Message);
        Assert.Contains("Stream1, Stream2", exception.Message);
    }

    [Fact]
    public async Task GetEventsAsync_with_sequence_number_filtering()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        ConfigureEventStoreDefaultStreamConfiguration(sut, grainId);
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        sut.AppendEvent(new StrEvent("First"));
        sut.AppendEvent(new IntEvent(1));
        sut.AppendEvent(new StrEvent("Second"));
        sut.AppendEvent(new IntEvent(2));
        sut.AppendEvent(new StrEvent("Third"));

        // Test FromSequenceNumber
        var eventsFromSeq2 = await sut.GetEventsAsync<IEvent>(
            new EventQueryOptions { FromSequenceNumber = 2 },
            TestContext.Current.CancellationToken).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(4, eventsFromSeq2.Count);
        Assert.Equal(new IntEvent(1), eventsFromSeq2[0]);
        Assert.Equal(new StrEvent("Second"), eventsFromSeq2[1]);

        // Test ToSequenceNumber
        var eventsToSeq3 = await sut.GetEventsAsync<IEvent>(
            new EventQueryOptions { ToSequenceNumber = 3 },
            TestContext.Current.CancellationToken).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(3, eventsToSeq3.Count);
        Assert.Equal(new StrEvent("First"), eventsToSeq3[0]);
        Assert.Equal(new IntEvent(1), eventsToSeq3[1]);
        Assert.Equal(new StrEvent("Second"), eventsToSeq3[2]);

        // Test FromSequenceNumber and ToSequenceNumber together
        var eventsRange = await sut.GetEventsAsync<IEvent>(
            new EventQueryOptions { FromSequenceNumber = 2, ToSequenceNumber = 4 },
            TestContext.Current.CancellationToken).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(3, eventsRange.Count);
        Assert.Equal(new IntEvent(1), eventsRange[0]);
        Assert.Equal(new StrEvent("Second"), eventsRange[1]);
        Assert.Equal(new IntEvent(2), eventsRange[2]);
    }

    [Fact]
    public async Task HasUnreactedEvents_is_false_when_no_reactors()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        ConfigureEventStoreDefaultStreamConfiguration(sut, grainId);
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.False(sut.HasUnreactedEvents);

        sut.AppendEvent(new StrEvent("Hello"));
        Assert.False(sut.HasUnreactedEvents);

        await sut.CommitAsync(TestContext.Current.CancellationToken);
        Assert.False(sut.HasUnreactedEvents);
    }

    [Fact]
    public async Task AppendEvent_with_reactor_sets_HasUnreactedEvents_to_true()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        var testReactor = new TestStrEventReactor();
        sut.Configure(
            grainId,
            new DummyGrain(),
            fixture.Services,
            builder => builder
                .AddStream<IEvent>()
                .Handle<StrEvent>((evt, pro) => pro with { StrValue = evt.Value })
                .Handle<IntEvent>((evt, pro) => pro with { IntValue = evt.Value })
                .React<StrEvent>("TestReactor", _ => testReactor));
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.False(sut.HasUnreactedEvents);

        sut.AppendEvent(new StrEvent("Hello"));

        Assert.True(sut.HasUnreactedEvents);
        Assert.Empty(testReactor.ProcessedEvents);
    }

    [Fact]
    public async Task ReactEventsAsync_processes_unreacted_events()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        var testReactor = new TestStrEventReactor();
        sut.Configure(
            grainId,
            new DummyGrain(),
            fixture.Services,
            builder => builder
                .AddStream<IEvent>()
                .React<StrEvent>("TestReactor", _ => testReactor));
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        sut.AppendEvent(new StrEvent("Hello"));
        sut.AppendEvent(new StrEvent("World"));
        Assert.True(sut.HasUnreactedEvents);

        await sut.ReactEventsAsync(new EventReactorContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);

        Assert.False(sut.HasUnreactedEvents);
        Assert.Equal(2, testReactor.ProcessedEvents.Count);
        Assert.Equal("Hello", testReactor.ProcessedEvents[0].Value);
        Assert.Equal("World", testReactor.ProcessedEvents[1].Value);
    }

    [Fact]
    public async Task GetEventsAsync_with_IsUnreacted_filter()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        var testReactor = new TestStrEventReactor();
        sut.Configure(
            grainId,
            new DummyGrain(),
            fixture.Services,
            builder => builder
                .AddStream<IEvent>()
                .React<StrEvent>("TestReactor", _ => testReactor));
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        sut.AppendEvent(new StrEvent("Hello"));
        sut.AppendEvent(new IntEvent(42)); // This should not have unreacted events since no reactor matches IntEvent
        sut.AppendEvent(new StrEvent("World"));

        // Test finding unreacted events
        var unreactedEvents = await sut.GetEventsAsync<IEvent>(new EventQueryOptions { IsUnreacted = true }, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expected: 2, unreactedEvents.Count);
        Assert.Equal("Hello", Assert.IsType<StrEvent>(unreactedEvents[0]).Value);
        Assert.Equal("World", Assert.IsType<StrEvent>(unreactedEvents[1]).Value);

        // Test finding reacted events (should be empty before ReactEventsAsync)
        var reactedEvents = await sut.GetEventsAsync<StrEvent>(new EventQueryOptions { IsUnreacted = false }, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Empty(reactedEvents);

        // React to events
        await sut.ReactEventsAsync(new EventReactorContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);

        // Now unreacted should be empty
        unreactedEvents = await sut.GetEventsAsync<IEvent>(new EventQueryOptions { IsUnreacted = true }, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Empty(unreactedEvents);

        // And reacted should contain the events
        reactedEvents = await sut.GetEventsAsync<StrEvent>(new EventQueryOptions { IsUnreacted = false }, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, reactedEvents.Count);
        Assert.Equal("Hello", reactedEvents[0].Value);
        Assert.Equal("World", reactedEvents[1].Value);
    }

    [Fact]
    public async Task ReactEventsAsync_with_multiple_reactors_on_same_stream()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        var firstReactor = new TestStrEventReactor();
        var secondReactor = new SecondTestStrEventReactor();
        sut.Configure(
            grainId,
            new DummyGrain(),
            fixture.Services,
            builder => builder
                .AddStream<IEvent>()
                .React<StrEvent>("FirstReactor", _ => firstReactor)
                .React<StrEvent>("SecondReactor", _ => secondReactor));
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        sut.AppendEvent(new StrEvent("Hello"));
        sut.AppendEvent(new StrEvent("World"));
        Assert.True(sut.HasUnreactedEvents);

        await sut.ReactEventsAsync(new EventReactorContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);

        Assert.False(sut.HasUnreactedEvents);

        // Both reactors should have processed all events
        Assert.Equal(2, firstReactor.ProcessedEvents.Count);
        Assert.Equal("Hello", firstReactor.ProcessedEvents[0].Value);
        Assert.Equal("World", firstReactor.ProcessedEvents[1].Value);

        Assert.Equal(2, secondReactor.ProcessedEvents.Count);
        Assert.Equal("Hello", secondReactor.ProcessedEvents[0].Value);
        Assert.Equal("World", secondReactor.ProcessedEvents[1].Value);
    }

    [Fact]
    public async Task ReactEventsAsync_with_failing_reactor_keeps_HasUnreactedEvents_true()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        var successfulReactor = new TestStrEventReactor();
        var failingReactor = new FailingTestReactor();
        sut.Configure(
            grainId,
            new DummyGrain(),
            fixture.Services,
            builder => builder
                .AddStream<IEvent>()
                .React<StrEvent>("SuccessfulReactor", _ => successfulReactor)
                .React<StrEvent>("FailingReactor", _ => failingReactor));
        await sut.InitializeAsync(TestContext.Current.CancellationToken);
        sut.AppendEvent(new StrEvent("Hello"));
        Assert.True(sut.HasUnreactedEvents);

        await sut.ReactEventsAsync(new EventReactorContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);

        Assert.True(sut.HasUnreactedEvents);
        Assert.Single(successfulReactor.ProcessedEvents);
        Assert.Equal("Hello", successfulReactor.ProcessedEvents[0].Value);
        Assert.Equal(1, failingReactor.CallCount);
    }

    [Fact]
    public async Task ReactEventsAsync_with_commit_and_persistence_lifecycle()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        var testReactor = new TestStrEventReactor();
        sut.Configure(
            grainId,
            new DummyGrain(),
            fixture.Services,
            builder => builder
                .AddStream<IEvent>()
                .Handle<StrEvent>((evt, pro) => pro with { StrValue = evt.Value })
                .Handle<IntEvent>((evt, pro) => pro with { IntValue = evt.Value })
                .React<StrEvent>("TestReactor", _ => testReactor));
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        // Append events (uncommitted first)
        sut.AppendEvent(new StrEvent("Hello"));
        sut.AppendEvent(new StrEvent("World"));
        Assert.True(sut.HasUnreactedEvents);

        // React to uncommitted events
        await sut.ReactEventsAsync(new EventReactorContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);
        Assert.False(sut.HasUnreactedEvents);

        // Verify reactor processed events before commit
        Assert.Equal(2, testReactor.ProcessedEvents.Count);
        Assert.Equal("Hello", testReactor.ProcessedEvents[0].Value);
        Assert.Equal("World", testReactor.ProcessedEvents[1].Value);

        // Commit events and reactor state
        await sut.CommitAsync(TestContext.Current.CancellationToken);

        // Create new EventStore instance to verify persistence
        var sut2 = CreateSut();
        var testReactor2 = new TestStrEventReactor();
        sut2.Configure(
            grainId,
            new DummyGrain(),
            fixture.Services,
            builder => builder
                .AddStream<IEvent>()
                .Handle<StrEvent>((evt, pro) => pro with { StrValue = evt.Value })
                .Handle<IntEvent>((evt, pro) => pro with { IntValue = evt.Value })
                .React<StrEvent>("TestReactor", _ => testReactor2));
        await sut2.InitializeAsync(TestContext.Current.CancellationToken);

        // Should not have unreacted events since they were processed and committed
        Assert.False(sut2.HasUnreactedEvents);
        Assert.Empty(testReactor2.ProcessedEvents); // New reactor instance shouldn't have processed events yet
    }

    private class DummyGrain : Grain, IGrainWithStringKey
    {
    }
}