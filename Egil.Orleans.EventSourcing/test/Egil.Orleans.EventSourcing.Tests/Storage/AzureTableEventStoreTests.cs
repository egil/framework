using Egil.Orleans.EventSourcing.Handlers;
using Egil.Orleans.EventSourcing.Tests;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Egil.Orleans.EventSourcing.Reactors;

namespace Egil.Orleans.EventSourcing.Storage;

public class AzureTableEventStoreTests(SiloFixture fixture) : IClassFixture<SiloFixture>
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

    // Failing handler for testing failure scenarios
    private class FailingIntEventHandler : IEventHandler<IntEvent, Projection>
    {
        public int CallCount { get; private set; }

        public ValueTask<Projection> HandleAsync(IntEvent @event, Projection projection, IEventHandlerContext context)
        {
            CallCount++;
            throw new InvalidOperationException("Handler failed intentionally");
        }
    }

    private static GrainId RandomGrainId([CallerMemberName] string memberName = "")
        => GrainId.Create(GrainType.Create("TestEventGrain"), IdSpan.Create($"{memberName}-{Guid.NewGuid().ToString("N")[0..8]}"));

    private AzureTableEventStore<Projection> CreateSut()
        => (AzureTableEventStore<Projection>)fixture.Services
        .GetRequiredService<IEventStoreFactory>()
        .CreateEventStore<Projection>(fixture.Services);

    private void ConfigureEventStoreDefaultStreamConfiguration(AzureTableEventStore<Projection> sut, GrainId grainId)
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
                .React("TestReactor", _ => testReactor));
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
                .React("TestReactor", _ => testReactor));
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
                .React("TestReactor", _ => testReactor));
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
                .React("FirstReactor", _ => firstReactor)
                .React("SecondReactor", _ => secondReactor));
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
                .React("SuccessfulReactor", _ => successfulReactor)
                .React("FailingReactor", _ => failingReactor));
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
                .React("TestReactor", _ => testReactor));
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
                .React("TestReactor", _ => testReactor2));
        await sut2.InitializeAsync(TestContext.Current.CancellationToken);

        // Should not have unreacted events since they were processed and committed
        Assert.False(sut2.HasUnreactedEvents);
        Assert.Empty(testReactor2.ProcessedEvents); // New reactor instance shouldn't have processed events yet
    }

    [Fact]
    public async Task ApplyEventsAsync_with_failing_handler_resets_projection_to_original_state()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        var failingHandler = new FailingIntEventHandler();
        sut.Configure(
            grainId,
            new DummyGrain(),
            fixture.Services,
            builder => builder
                .AddStream<IEvent>()
                .Handle<StrEvent>((evt, pro) => pro with { StrValue = evt.Value })
                .Handle(_ => failingHandler));
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        // Set initial state
        sut.AppendEvent(new StrEvent("Initial"));
        await sut.ApplyEventsAsync(new EventHandlerContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);
        var expectedProjection = new Projection(StrValue: "Initial", IntValue: 0);
        Assert.Equal(expectedProjection, sut.Projection);
        Assert.False(sut.HasUnappliedEvents);

        // Commit the initial state
        await sut.CommitAsync(TestContext.Current.CancellationToken);

        // Add events - one that succeeds and one that fails
        sut.AppendEvent(new StrEvent("BeforeFailure"));
        sut.AppendEvent(new IntEvent(42)); // This will fail
        sut.AppendEvent(new StrEvent("AfterFailure"));

        // Apply events should fail and reset projection to the state before ApplyEventsAsync
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ApplyEventsAsync(new EventHandlerContext<Projection>(sut, grainId), TestContext.Current.CancellationToken).AsTask());
        
        Assert.Contains("Handler failed intentionally", exception.Message);
        Assert.Equal(1, failingHandler.CallCount);

        // Projection should be reset to the state it was in before ApplyEventsAsync was called
        Assert.Equal(expectedProjection, sut.Projection);
        
        // Should still have unapplied events since ApplyEventsAsync failed
        Assert.True(sut.HasUnappliedEvents);
    }

    [Fact]
    public async Task ApplyEventsAsync_with_multiple_failing_handlers_resets_projection_after_first_failure()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        var firstFailingHandler = new FailingIntEventHandler();
        var secondFailingHandler = new FailingIntEventHandler();
        sut.Configure(
            grainId,
            new DummyGrain(),
            fixture.Services,
            builder => builder
                .AddStream<IEvent>()
                .Handle<StrEvent>((evt, pro) => pro with { StrValue = evt.Value })
                .Handle(_ => firstFailingHandler)
                .Handle(_ => secondFailingHandler));
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        // Set initial state and commit
        sut.AppendEvent(new StrEvent("Initial"));
        await sut.ApplyEventsAsync(new EventHandlerContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);
        await sut.CommitAsync(TestContext.Current.CancellationToken);
        var expectedProjection = new Projection(StrValue: "Initial", IntValue: 0);

        // Add events that will cause failures
        sut.AppendEvent(new IntEvent(42)); // Both handlers will fail

        // Apply events should fail on first handler and not call second handler
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ApplyEventsAsync(new EventHandlerContext<Projection>(sut, grainId), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("Handler failed intentionally", exception.Message);
        Assert.Equal(1, firstFailingHandler.CallCount);
        Assert.Equal(0, secondFailingHandler.CallCount); // Should not be called due to early exit

        // Projection should be reset to the state before ApplyEventsAsync
        Assert.Equal(expectedProjection, sut.Projection);
        Assert.True(sut.HasUnappliedEvents);
    }

    [Fact]
    public async Task ApplyEventsAsync_with_partial_success_then_failure_resets_entire_batch()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        var appliedEvents = new List<string>();
        var failingHandler = new FailingIntEventHandler();
        
        sut.Configure(
            grainId,
            new DummyGrain(),
            fixture.Services,
            builder => builder
                .AddStream<IEvent>()
                .Handle<StrEvent>((evt, pro) => 
                {
                    appliedEvents.Add($"StrEvent:{evt.Value}");
                    return pro with { StrValue = evt.Value };
                })
                .Handle(_ => failingHandler));
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        // Set initial state and commit
        sut.AppendEvent(new StrEvent("Initial"));
        await sut.ApplyEventsAsync(new EventHandlerContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);
        await sut.CommitAsync(TestContext.Current.CancellationToken);
        var originalProjection = new Projection(StrValue: "Initial", IntValue: 0);

        // Add multiple events - some will succeed, one will fail
        sut.AppendEvent(new StrEvent("First"));   // Should succeed but be rolled back
        sut.AppendEvent(new StrEvent("Second"));  // Should succeed but be rolled back  
        sut.AppendEvent(new IntEvent(42));        // Should fail
        sut.AppendEvent(new StrEvent("Third"));   // Should not be processed due to early exit

        // Apply events should fail and reset entire projection
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ApplyEventsAsync(new EventHandlerContext<Projection>(sut, grainId), TestContext.Current.CancellationToken).AsTask());
        
        Assert.Contains("Handler failed intentionally", exception.Message);
        Assert.Equal(1, failingHandler.CallCount);

        // Check that successful events were processed but rolled back
        Assert.Contains("StrEvent:First", appliedEvents);
        Assert.Contains("StrEvent:Second", appliedEvents);
        Assert.DoesNotContain("StrEvent:Third", appliedEvents); // Not processed due to failure

        // Projection should be completely reset to the original state
        Assert.Equal(originalProjection, sut.Projection);
        
        // Should still have unapplied events
        Assert.True(sut.HasUnappliedEvents);
    }

    private class DummyGrain : Grain, IGrainWithStringKey
    {
    }

    // Test handler for base interface IEvent
    private class IEventHandler : IEventHandler<IEvent, Projection>
    {
        public List<IEvent> ProcessedEvents { get; } = new();

        public ValueTask<Projection> HandleAsync(IEvent @event, Projection projection, IEventHandlerContext context)
        {
            ProcessedEvents.Add(@event);
            
            // Apply the event to the projection based on its concrete type
            return @event switch
            {
                StrEvent strEvent => ValueTask.FromResult(projection with { StrValue = strEvent.Value }),
                IntEvent intEvent => ValueTask.FromResult(projection with { IntValue = intEvent.Value }),
                _ => ValueTask.FromResult(projection)
            };
        }
    }

    // Test reactor for base interface IEvent
    private class IEventReactor : IEventReactor<IEvent, Projection>
    {
        public string Id { get; } = "IEventReactor";
        public List<IEvent> ProcessedEvents { get; } = new();

        public ValueTask ReactAsync(IEnumerable<IEvent> events, Projection projection, IEventReactContext context, CancellationToken cancellationToken = default)
        {
            ProcessedEvents.AddRange(events);
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Handler_for_base_interface_processes_all_derived_event_types()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        var baseHandler = new IEventHandler();
        
        sut.Configure(
            grainId,
            new DummyGrain(),
            fixture.Services,
            builder => builder
                .AddStream<IEvent>()
                .Handle<IEvent>(_ => baseHandler));
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        // Append different derived event types
        sut.AppendEvent(new StrEvent("Hello"));
        sut.AppendEvent(new IntEvent(42));
        sut.AppendEvent(new StrEvent("World"));

        // Apply events - the base handler should process all derived types
        await sut.ApplyEventsAsync(new EventHandlerContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);

        // Verify all events were processed by the base handler
        Assert.Equal(3, baseHandler.ProcessedEvents.Count);
        Assert.IsType<StrEvent>(baseHandler.ProcessedEvents[0]);
        Assert.Equal("Hello", ((StrEvent)baseHandler.ProcessedEvents[0]).Value);
        Assert.IsType<IntEvent>(baseHandler.ProcessedEvents[1]);
        Assert.Equal(42, ((IntEvent)baseHandler.ProcessedEvents[1]).Value);
        Assert.IsType<StrEvent>(baseHandler.ProcessedEvents[2]);
        Assert.Equal("World", ((StrEvent)baseHandler.ProcessedEvents[2]).Value);

        // Verify projection was correctly updated
        Assert.Equal(new Projection(StrValue: "World", IntValue: 42), sut.Projection);
        Assert.False(sut.HasUnappliedEvents);
    }

    [Fact]
    public async Task Reactor_for_base_interface_processes_all_derived_event_types()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        var baseReactor = new IEventReactor();
        
        sut.Configure(
            grainId,
            new DummyGrain(),
            fixture.Services,
            builder => builder
                .AddStream<IEvent>()
                .React<IEvent>("BaseReactor", _ => baseReactor));
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        // Append different derived event types
        sut.AppendEvent(new StrEvent("Hello"));
        sut.AppendEvent(new IntEvent(42));
        sut.AppendEvent(new StrEvent("World"));

        Assert.True(sut.HasUnreactedEvents);

        // React to events - the base reactor should process all derived types
        await sut.ReactEventsAsync(new EventReactorContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);

        // Verify all events were processed by the base reactor
        Assert.Equal(3, baseReactor.ProcessedEvents.Count);
        Assert.IsType<StrEvent>(baseReactor.ProcessedEvents[0]);
        Assert.Equal("Hello", ((StrEvent)baseReactor.ProcessedEvents[0]).Value);
        Assert.IsType<IntEvent>(baseReactor.ProcessedEvents[1]);
        Assert.Equal(42, ((IntEvent)baseReactor.ProcessedEvents[1]).Value);
        Assert.IsType<StrEvent>(baseReactor.ProcessedEvents[2]);
        Assert.Equal("World", ((StrEvent)baseReactor.ProcessedEvents[2]).Value);

        Assert.False(sut.HasUnreactedEvents);
    }

    [Fact]
    public async Task Multiple_handlers_for_base_and_derived_types_all_process_matching_events()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        var baseHandler = new IEventHandler();
        var appliedEvents = new List<string>();
        
        sut.Configure(
            grainId,
            new DummyGrain(),
            fixture.Services,
            builder => builder
                .AddStream<IEvent>()
                .Handle<IEvent>(_ => baseHandler) // Base handler
                .Handle<StrEvent>((evt, pro) => // Specific handler for StrEvent
                {
                    appliedEvents.Add($"StrEventHandler:{evt.Value}");
                    return pro;
                })
                .Handle<IntEvent>((evt, pro) => // Specific handler for IntEvent
                {
                    appliedEvents.Add($"IntEventHandler:{evt.Value}");
                    return pro;
                }));
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        // Append different event types
        sut.AppendEvent(new StrEvent("Hello"));
        sut.AppendEvent(new IntEvent(42));

        // Apply events - all matching handlers should process the events
        await sut.ApplyEventsAsync(new EventHandlerContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);

        // Verify base handler processed all events
        Assert.Equal(2, baseHandler.ProcessedEvents.Count);
        Assert.IsType<StrEvent>(baseHandler.ProcessedEvents[0]);
        Assert.IsType<IntEvent>(baseHandler.ProcessedEvents[1]);

        // Verify specific handlers also processed their respective events
        Assert.Contains("StrEventHandler:Hello", appliedEvents);
        Assert.Contains("IntEventHandler:42", appliedEvents);

        // Verify final projection state (base handler updates projection)
        Assert.Equal(new Projection(StrValue: "Hello", IntValue: 42), sut.Projection);
    }

    [Fact]
    public async Task Multiple_reactors_for_base_and_derived_types_all_process_matching_events()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        var baseReactor = new IEventReactor();
        var strReactor = new TestStrEventReactor();
        
        sut.Configure(
            grainId,
            new DummyGrain(),
            fixture.Services,
            builder => builder
                .AddStream<IEvent>()
                .React<IEvent>("BaseReactor", _ => baseReactor) // Base reactor
                .React("StrReactor", _ => strReactor)); // Specific reactor for StrEvent
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        // Append different event types
        sut.AppendEvent(new StrEvent("Hello"));
        sut.AppendEvent(new IntEvent(42));
        sut.AppendEvent(new StrEvent("World"));

        Assert.True(sut.HasUnreactedEvents);

        // React to events
        await sut.ReactEventsAsync(new EventReactorContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);

        // Verify base reactor processed all events
        Assert.Equal(3, baseReactor.ProcessedEvents.Count);
        Assert.IsType<StrEvent>(baseReactor.ProcessedEvents[0]);
        Assert.IsType<IntEvent>(baseReactor.ProcessedEvents[1]);
        Assert.IsType<StrEvent>(baseReactor.ProcessedEvents[2]);

        // Verify specific reactor processed only StrEvents
        Assert.Equal(2, strReactor.ProcessedEvents.Count);
        Assert.Equal("Hello", strReactor.ProcessedEvents[0].Value);
        Assert.Equal("World", strReactor.ProcessedEvents[1].Value);

        Assert.False(sut.HasUnreactedEvents);
    }

    // Test with a more complex hierarchy
    private interface IBaseEvent;
    private interface IUserEvent : IBaseEvent;
    private interface ISystemEvent : IBaseEvent;
    
    [JsonDerivedType(typeof(UserLoginEvent), "UserLoginEvent.V1")]
    [JsonDerivedType(typeof(UserLogoutEvent), "UserLogoutEvent.V1")]
    [JsonDerivedType(typeof(SystemStartEvent), "SystemStartEvent.V1")]
    private record class UserLoginEvent(string UserId) : IUserEvent;
    private record class UserLogoutEvent(string UserId) : IUserEvent;
    private record class SystemStartEvent(DateTime Timestamp) : ISystemEvent;

    private class HierarchyTestHandler : IEventHandler<IBaseEvent, Projection>
    {
        public List<IBaseEvent> ProcessedEvents { get; } = new();

        public ValueTask<Projection> HandleAsync(IBaseEvent @event, Projection projection, IEventHandlerContext context)
        {
            ProcessedEvents.Add(@event);
            return ValueTask.FromResult(projection);
        }
    }

    private class UserEventHandler : IEventHandler<IUserEvent, Projection>
    {
        public List<IUserEvent> ProcessedEvents { get; } = new();

        public ValueTask<Projection> HandleAsync(IUserEvent @event, Projection projection, IEventHandlerContext context)
        {
            ProcessedEvents.Add(@event);
            return ValueTask.FromResult(projection);
        }
    }

    private class HierarchyTestReactor : IEventReactor<IBaseEvent, Projection>
    {
        public string Id { get; } = "HierarchyTestReactor";
        public List<IBaseEvent> ProcessedEvents { get; } = new();

        public ValueTask ReactAsync(IEnumerable<IBaseEvent> events, Projection projection, IEventReactContext context, CancellationToken cancellationToken = default)
        {
            ProcessedEvents.AddRange(events);
            return ValueTask.CompletedTask;
        }
    }

    private class UserEventReactor : IEventReactor<IUserEvent, Projection>
    {
        public string Id { get; } = "UserEventReactor";
        public List<IUserEvent> ProcessedEvents { get; } = new();

        public ValueTask ReactAsync(IEnumerable<IUserEvent> events, Projection projection, IEventReactContext context, CancellationToken cancellationToken = default)
        {
            ProcessedEvents.AddRange(events);
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Handler_hierarchy_processes_events_correctly()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        var baseHandler = new HierarchyTestHandler();
        var userHandler = new UserEventHandler();
        
        sut.Configure(
            grainId,
            new DummyGrain(),
            fixture.Services,
            builder => builder
                .AddStream<IBaseEvent>()
                .Handle<IBaseEvent>(_ => baseHandler) // Should process all events
                .Handle(_ => userHandler)); // Should process only user events
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        // Append events from different levels of the hierarchy
        sut.AppendEvent(new UserLoginEvent("user1"));
        sut.AppendEvent(new SystemStartEvent(DateTime.UtcNow));
        sut.AppendEvent(new UserLogoutEvent("user1"));

        // Apply events
        await sut.ApplyEventsAsync(new EventHandlerContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);

        // Verify base handler processed all events
        Assert.Equal(3, baseHandler.ProcessedEvents.Count);
        Assert.IsType<UserLoginEvent>(baseHandler.ProcessedEvents[0]);
        Assert.IsType<SystemStartEvent>(baseHandler.ProcessedEvents[1]);
        Assert.IsType<UserLogoutEvent>(baseHandler.ProcessedEvents[2]);

        // Verify user handler processed only user events
        Assert.Equal(2, userHandler.ProcessedEvents.Count);
        Assert.IsType<UserLoginEvent>(userHandler.ProcessedEvents[0]);
        Assert.IsType<UserLogoutEvent>(userHandler.ProcessedEvents[1]);
    }

    [Fact]
    public async Task Reactor_hierarchy_processes_events_correctly()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        var baseReactor = new HierarchyTestReactor();
        var userReactor = new UserEventReactor();
        
        sut.Configure(
            grainId,
            new DummyGrain(),
            fixture.Services,
            builder => builder
                .AddStream<IBaseEvent>()
                .React<IBaseEvent>("BaseReactor", _ => baseReactor) // Should process all events
                .React("UserReactor", _ => userReactor)); // Should process only user events
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        // Append events from different levels of the hierarchy
        sut.AppendEvent(new UserLoginEvent("user1"));
        sut.AppendEvent(new SystemStartEvent(DateTime.UtcNow));
        sut.AppendEvent(new UserLogoutEvent("user1"));

        Assert.True(sut.HasUnreactedEvents);

        // React to events
        await sut.ReactEventsAsync(new EventReactorContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);

        // Verify base reactor processed all events
        Assert.Equal(3, baseReactor.ProcessedEvents.Count);
        Assert.IsType<UserLoginEvent>(baseReactor.ProcessedEvents[0]);
        Assert.IsType<SystemStartEvent>(baseReactor.ProcessedEvents[1]);
        Assert.IsType<UserLogoutEvent>(baseReactor.ProcessedEvents[2]);

        // Verify user reactor processed only user events
        Assert.Equal(2, userReactor.ProcessedEvents.Count);
        Assert.IsType<UserLoginEvent>(userReactor.ProcessedEvents[0]);
        Assert.IsType<UserLogoutEvent>(userReactor.ProcessedEvents[1]);

        Assert.False(sut.HasUnreactedEvents);
    }

    [Fact]
    public async Task Handler_for_object_type_processes_all_events()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        var objectHandler = new List<object>();
        
        sut.Configure(
            grainId,
            new DummyGrain(),
            fixture.Services,
            builder => builder
                .AddStream<object>()
                .Handle<object>((evt, pro) =>
                {
                    objectHandler.Add(evt);
                    return pro;
                }));
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        // Append events of different types - they should all be captured as objects
        sut.AppendEvent("StringEvent");
        sut.AppendEvent(123);
        sut.AppendEvent(new { Name = "Anonymous", Value = 42 });

        await sut.ApplyEventsAsync(new EventHandlerContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);

        // Verify all events were processed
        Assert.Equal(3, objectHandler.Count);
        Assert.Equal("StringEvent", objectHandler[0]);
        Assert.Equal(123, objectHandler[1]);
        Assert.Equal("Anonymous", objectHandler[2].GetType().GetProperty("Name")?.GetValue(objectHandler[2]));
    }

    [Fact]
    public async Task Mixed_handler_specificity_processes_correctly()
    {
        var grainId = RandomGrainId();
        var sut = CreateSut();
        var processedEventTypes = new List<string>();
        
        sut.Configure(
            grainId,
            new DummyGrain(),
            fixture.Services,
            builder => builder
                .AddStream<IEvent>()
                .Handle<IEvent>((evt, pro) =>
                {
                    processedEventTypes.Add($"IEvent:{evt.GetType().Name}");
                    return pro;
                })
                .Handle<StrEvent>((evt, pro) =>
                {
                    processedEventTypes.Add($"StrEvent:{evt.Value}");
                    return pro;
                }));
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        sut.AppendEvent(new StrEvent("Test"));
        sut.AppendEvent(new IntEvent(100));

        await sut.ApplyEventsAsync(new EventHandlerContext<Projection>(sut, grainId), TestContext.Current.CancellationToken);

        // Both handlers should process the StrEvent
        Assert.Contains("IEvent:StrEvent", processedEventTypes);
        Assert.Contains("StrEvent:Test", processedEventTypes);
        
        // Only the base handler should process the IntEvent
        Assert.Contains("IEvent:IntEvent", processedEventTypes);
        Assert.DoesNotContain("StrEvent:100", processedEventTypes); // This shouldn't exist as IntEvent isn't StrEvent
    }
}