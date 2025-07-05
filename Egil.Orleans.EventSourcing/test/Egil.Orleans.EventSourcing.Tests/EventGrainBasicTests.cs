using Xunit;

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
}

/// <summary>
/// Test implementation of EventGrain for testing basic functionality.
/// </summary>
public class TestEventGrain : EventGrain<TestEvent, TestProjection>
{
    public TestProjection TestProjection => Projection;
    public IEventStorage TestEventStorage => EventStorage;
    public bool IsActivated { get; private set; }

    public TestEventGrain(IEventStorage eventStorage) : base(eventStorage)
    {
    }

    public async Task TestActivateAsync()
    {
        await OnActivateAsync(CancellationToken.None);
        IsActivated = true;
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
/// Fake implementation of IEventStorage for testing.
/// </summary>
public class FakeEventStorage : IEventStorage
{
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

    public ValueTask SaveAtomicallyAsync<TProjection>(string grainId, IEnumerable<object> events, TProjection projection, CancellationToken cancellationToken = default) 
        where TProjection : class
    {
        // For testing, just return completed task
        return ValueTask.CompletedTask;
    }
}
