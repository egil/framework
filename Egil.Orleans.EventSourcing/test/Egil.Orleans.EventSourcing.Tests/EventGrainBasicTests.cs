namespace Egil.Orleans.EventSourcing.Tests;

/// <summary>
/// Tests for basic EventGrain functionality and initialization.
/// </summary>
public class EventGrainBasicTests
{
    [Fact]
    public void Default_projection_is_initialized()
    {
        // Arrange
        var eventStorage = new FakeEventStorage();

        // Act
        var grain = new TestEventGrain(eventStorage);

        // Assert
        Assert.NotNull(grain);
        Assert.NotNull(grain.TestProjection);
        Assert.Equal("Default", grain.TestProjection.Name);
        Assert.Equal(0, grain.TestProjection.Version);
    }

    [Fact]
    public void Event_storage_dependency_is_injected()
    {
        // Arrange
        var eventStorage = new FakeEventStorage();

        // Act
        var grain = new TestEventGrain(eventStorage);

        // Assert
        Assert.Same(eventStorage, grain.TestEventStorage);
    }

    [Fact]
    public async Task Empty_event_stream_loads_default_projection()
    {
        // Arrange
        var eventStorage = new FakeEventStorage();
        var grain = new TestEventGrain(eventStorage);

        // Act
        await grain.TestActivateAsync();

        // Assert
        Assert.NotNull(grain.TestProjection);
        Assert.Equal("Default", grain.TestProjection.Name);
        Assert.Equal(0, grain.TestProjection.Version);
        Assert.True(grain.IsActivated);
    }

    [Fact]
    public async Task Single_event_is_processed()
    {
        // Arrange
        var eventStorage = new FakeEventStorage();
        var grain = new TestEventGrain(eventStorage);
        var createdEvent = new TestCreatedEvent("TestGrain");

        // Act
        await grain.TestProcessEventsAsync(createdEvent);

        // Assert
        Assert.Single(eventStorage.SavedEvents);
        Assert.Contains(createdEvent, eventStorage.SavedEvents);
        Assert.NotNull(eventStorage.SavedProjection);
    }

    [Fact]
    public async Task Event_handlers_are_called_during_processing()
    {
        // Arrange
        var eventStorage = new FakeEventStorage();
        var grain = new TestEventGrain(eventStorage);
        var createdEvent = new TestCreatedEvent("TestGrain");

        // Act
        await grain.TestProcessEventsAsync(createdEvent);

        // Assert
        Assert.True(grain.WasEventHandlerCalled);
        Assert.Equal("TestGrain", grain.TestProjection.Name);
        Assert.Equal(1, grain.TestProjection.Version);
    }

    [Fact]
    public async Task Projection_updates_with_new_events()
    {
        // Arrange
        var eventStorage = new FakeEventStorage();
        var grain = new TestEventGrain(eventStorage);
        var createdEvent = new TestCreatedEvent("InitialName");
        var updatedEvent = new TestUpdatedEvent("UpdatedName");

        // Act
        await grain.TestProcessEventsAsync(createdEvent);
        await grain.TestProcessEventsAsync(updatedEvent);

        // Assert
        Assert.Equal("UpdatedName", grain.TestProjection.Name);
        Assert.Equal(2, grain.TestProjection.Version);
        Assert.Equal(2, eventStorage.SavedEvents.Count);
    }
}

/// <summary>
/// Test implementation of EventGrain for testing basic functionality.
/// </summary>
public class TestEventGrain : EventGrain<TestEvent, TestProjection>
{
    public TestProjection TestProjection => Projection;
    public IEventStorage TestEventStorage => EventStorage;
    public bool IsActivated { get; private set; }
    public bool WasEventHandlerCalled { get; private set; }

    public TestEventGrain(IEventStorage eventStorage) : base(eventStorage)
    {
    }

    public async Task TestActivateAsync()
    {
        await OnActivateAsync(CancellationToken.None);
        IsActivated = true;
    }

    public async Task TestProcessEventsAsync(params TestEvent[] events)
    {
        await ProcessEventsAsync(events);
    }

    public async Task ProcessEventAsync(TestEvent @event, CancellationToken cancellationToken)
    {
        await ProcessEventsAsync(@event);
    }

    protected override TestProjection ApplyEvents(IEnumerable<TestEvent> events, TestProjection projection)
    {
        var updatedProjection = projection;

        foreach (var @event in events)
        {
            if (@event is TestCreatedEvent createdEvent)
            {
                updatedProjection = new TestProjection
                {
                    Name = createdEvent.Name,
                    Version = updatedProjection.Version + 1
                };

                WasEventHandlerCalled = true;
            }
            else if (@event is TestUpdatedEvent updatedEvent)
            {
                updatedProjection = new TestProjection
                {
                    Name = updatedEvent.Name,
                    Version = updatedProjection.Version + 1
                };

                WasEventHandlerCalled = true;
            }
        }

        return updatedProjection;
    }
}

/// <summary>
/// Test projection for basic EventGrain tests.
/// </summary>
public class TestProjection : IEventProjection<TestProjection>
{
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; } = 0;

    public static TestProjection CreateDefault()
    {
        return new TestProjection
        {
            Name = "Default",
            Version = 0
        };
    }
}

/// <summary>
/// Base class for test events.
/// </summary>
public abstract record TestEvent;

/// <summary>
/// Test event for basic functionality.
/// </summary>
public record TestCreatedEvent(string Name) : TestEvent;

/// <summary>
/// Test event for updating data.
/// </summary>
public record TestUpdatedEvent(string Name) : TestEvent;

/// <summary>
/// Fake implementation of IEventStorage for testing.
/// </summary>
public class FakeEventStorage : IEventStorage
{
    public List<object> SavedEvents { get; } = new();
    public object? SavedProjection { get; private set; }

    public ValueTask<TProjection?> LoadProjectionAsync<TProjection>(string grainId, CancellationToken cancellationToken = default)
        where TProjection : class
    {
        // Return null to simulate empty storage for now
        return ValueTask.FromResult<TProjection?>(null);
    }

    public IAsyncEnumerable<TEvent> LoadEventsAsync<TEvent>(string grainId, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        // Return empty async enumerable to simulate no events
        return AsyncEnumerable.Empty<TEvent>();
    }

    public ValueTask SaveAsync<TProjection>(string grainId, IEnumerable<object> events, TProjection projection, CancellationToken cancellationToken = default)
        where TProjection : class
    {
        // Capture saved events and projection for verification in tests
        foreach (var @event in events)
        {
            SavedEvents.Add(@event);
        }

        SavedProjection = projection;

        return ValueTask.CompletedTask;
    }
}
