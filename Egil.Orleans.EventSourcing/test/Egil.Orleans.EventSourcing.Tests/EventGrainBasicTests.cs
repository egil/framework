using Xunit;

namespace Egil.Orleans.EventSourcing.Tests;

/// <summary>
/// Tests for basic EventGrain functionality and initialization.
/// </summary>
public class EventGrainBasicTests
{
    [Fact]
    public void EventGrain_CanBeCreated_WithBasicProjection()
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
    public void EventGrain_StoresEventStorageDependency()
    {
        // Arrange
        var eventStorage = new FakeEventStorage();
        
        // Act
        var grain = new TestEventGrain(eventStorage);
        
        // Assert
        Assert.Same(eventStorage, grain.TestEventStorage);
    }
}

/// <summary>
/// Test implementation of EventGrain for testing basic functionality.
/// </summary>
public class TestEventGrain : EventGrain<TestEvent, TestProjection>
{
    public TestProjection TestProjection => Projection;
    public IEventStorage TestEventStorage => EventStorage;
    
    public TestEventGrain(IEventStorage eventStorage) : base(eventStorage)
    {
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
    // Implementation will be added as we develop the IEventStorage interface
}
