using Egil.Orleans.EventSourcing.Examples;

namespace Egil.Orleans.EventSourcing.Tests;

/// <summary>
/// TDD tests for event handler registration and execution.
/// </summary>
#pragma warning disable xUnit1051 // Calls to methods which accept CancellationToken should use TestContext.Current.CancellationToken
public class EventGrainTests(SiloFixture fixture) : IClassFixture<SiloFixture>
{
    private CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Event_processing_invokes_configured_handler()
    {
        // Arrange - Test that handler registration and execution works
        var eventStorage = fixture.EventStorage;
        var grain = fixture.GetGrain<ITestEventGrainWithHandlerGrain>(Guid.NewGuid());
        var userCreatedEvent = new UserCreated("user-123", "John Doe", "john.doe@example.com", DateTimeOffset.UtcNow);

        // Act - Process the event
        await grain.SendEventsAsync(userCreatedEvent);

        // Assert - Handler should have updated the projection
        var projection = await grain.GetProjectionAsync();
        Assert.Equal("John Doe", projection.Name);
        Assert.Equal("john.doe@example.com", projection.Email);
        Assert.Equal(1, projection.Version);

        // Verify event was saved
        var grainEvents = await eventStorage.LoadEventsAsync<IUserEvent>(grain.GetGrainId()).ToArrayAsync();
        Assert.Single(grainEvents);
        Assert.Contains(userCreatedEvent, grainEvents);
    }

    [Fact]
    public async Task Multiple_events_are_processed_by_correct_handlers()
    {
        // Arrange - Test multiple event types with different handlers
        var grain = fixture.GetGrain<ITestEventGrainWithMultipleHandlersGrain>(Guid.NewGuid());

        var userCreatedEvent = new UserCreated("user-123", "John Doe", "john.doe@example.com", DateTimeOffset.UtcNow);
        var userMessageEvent = new UserMessageReceived("user-123", "Hello World", DateTimeOffset.UtcNow);

        // Act - Process multiple events
        await grain.SendEventsAsync(userCreatedEvent, userMessageEvent);

        // Assert - Both handlers should have been invoked
        var projection = await grain.GetProjectionAsync();
        Assert.Equal("John Doe", projection.Name);
        Assert.Equal("john.doe@example.com", projection.Email);
        Assert.Equal(1, projection.TotalMessagesCount);
        Assert.Equal(2, projection.Version);
    }

    [Fact]
    public async Task Handler_receives_event_and_current_projection()
    {
        // Arrange - Test that handlers receive correct parameters
        var grain = fixture.GetGrain<ITestEventGrainWithValidationHandlerGrain>(Guid.NewGuid());
        await fixture.EventStorage.SaveAsync<IUserEvent, UserProjectionWithVersion>(
            grain.GetGrainId(),
            [],
            new UserProjectionWithVersion(
                Name: "Initial Name",
                Email: "initial@example.com",
                TotalMessagesCount: 0,
                Version: 5,
                IsDeactivated: false,
                HandlerValidationPassed: false),
                cancellationToken);

        var userCreatedEvent = new UserCreated("user-123", "New Name", "new@example.com", DateTimeOffset.UtcNow);

        // Act - Process the event
        await grain.SendEventsAsync(userCreatedEvent);

        // Assert - Handler should have validated initial state and updated projection
        var projection = await grain.GetProjectionAsync();
        Assert.Equal("New Name", projection.Name);
        Assert.Equal(6, projection.Version); // Should increment from 5
        Assert.True(projection.HandlerValidationPassed);
    }

    [Fact]
    public async Task Partition_filters_events_by_type()
    {
        // Arrange - Test that partitions only process their configured event types
        var grain = fixture.GetGrain<ITestEventGrainWithPartitionFilteringGrain>(Guid.NewGuid());
        var userCreatedEvent = new UserCreated("user-123", "Test User", "test@example.com", DateTimeOffset.UtcNow);
        var userDeactivatedEvent = new UserDeactivated("user-123", "test reason", DateTimeOffset.UtcNow); // Different event type

        // Act - Process both events
        await grain.SendEventsAsync(userCreatedEvent, userDeactivatedEvent);

        // Assert - Only the UserCreated should have been processed
        var projection = await grain.GetProjectionAsync();
        Assert.Equal("Test User", projection.Name);
        Assert.Equal(1, projection.Version); // Only one event processed
        Assert.False(projection.IsDeactivated); // Deactivated event not processed
    }
}

// Test implementations with different handler configurations

public interface ITestEventGrainWithHandlerGrain : IGrainWithGuidKey
{
    Task<UserProjectionWithVersion> GetProjectionAsync();

    Task SendEventsAsync(params IUserEvent[] events);
}

/// <summary>
/// Test grain with a simple event handler configured.
/// </summary>
public class TestEventGrainWithHandler : EventGrain<IUserEvent, UserProjectionWithVersion>, ITestEventGrainWithHandlerGrain
{
    public TestEventGrainWithHandler(IEventStorage eventStorage) : base(eventStorage) { }

    public Task<UserProjectionWithVersion> GetProjectionAsync() => Task.FromResult(Projection);

    public Task SendEventsAsync(params IUserEvent[] events) => ProcessEventsAsync(events);

    static TestEventGrainWithHandler()
    {
        Configure<TestEventGrainWithHandler>(builder =>
        {
            builder.AddPartition<UserCreated>()
                .Handle<UserCreated>((evt, projection) =>
                {
                    return projection with
                    {
                        Name = evt.Name,
                        Email = evt.Email,
                        Version = projection.Version + 1
                    };
                });
        });
    }
}


public interface ITestEventGrainWithMultipleHandlersGrain : IGrainWithGuidKey
{
    Task<UserProjectionWithVersion> GetProjectionAsync();

    Task SendEventsAsync(params IUserEvent[] events);
}

/// <summary>
/// Test grain with multiple event handlers configured.
/// </summary>
public class TestEventGrainWithMultipleHandlers : EventGrain<IUserEvent, UserProjectionWithVersion>, ITestEventGrainWithMultipleHandlersGrain
{
    public TestEventGrainWithMultipleHandlers(IEventStorage eventStorage) : base(eventStorage) { }

    public Task<UserProjectionWithVersion> GetProjectionAsync() => Task.FromResult(Projection);

    public Task SendEventsAsync(params IUserEvent[] events) => ProcessEventsAsync(events);

    static TestEventGrainWithMultipleHandlers()
    {
        Configure<TestEventGrainWithMultipleHandlers>(builder =>
        {
            builder.AddPartition<UserCreated>()
                .Handle<UserCreated>((evt, projection) =>
                {
                    return projection with
                    {
                        Name = evt.Name,
                        Email = evt.Email,
                        Version = projection.Version + 1
                    };
                });

            builder.AddPartition<UserMessageReceived>()
                .Handle<UserMessageReceived>((evt, projection) =>
                {
                    return projection with
                    {
                        TotalMessagesCount = projection.TotalMessagesCount + 1,
                        Version = projection.Version + 1
                    };
                });
        });
    }
}

public interface ITestEventGrainWithValidationHandlerGrain : IGrainWithGuidKey
{
    Task<UserProjectionWithVersion> GetProjectionAsync();

    Task SendEventsAsync(params IUserEvent[] events);
}

/// <summary>
/// Test grain that validates handler receives correct parameters.
/// </summary>
public class TestEventGrainWithValidationHandler(IEventStorage eventStorage) : EventGrain<IUserEvent, UserProjectionWithVersion>(eventStorage), ITestEventGrainWithValidationHandlerGrain
{
    public Task<UserProjectionWithVersion> GetProjectionAsync() => Task.FromResult(Projection);

    public Task SendEventsAsync(params IUserEvent[] events) => ProcessEventsAsync(events);

    static TestEventGrainWithValidationHandler()
    {
        Configure<TestEventGrainWithValidationHandler>(builder =>
        {
            builder.AddPartition<UserCreated>()
                .Handle<UserCreated>((evt, projection) =>
                {
                    var validationPassed = projection.Name == "Initial Name" && projection.Version == 5;

                    return projection with
                    {
                        Name = evt.Name,
                        Email = evt.Email,
                        Version = projection.Version + 1,
                        HandlerValidationPassed = validationPassed
                    };
                });
        });
    }
}

public interface ITestEventGrainWithPartitionFilteringGrain : IGrainWithGuidKey
{
    Task<UserProjectionWithVersion> GetProjectionAsync();

    Task SendEventsAsync(params IUserEvent[] events);
}

/// <summary>
/// Test grain that demonstrates partition filtering by event type.
/// </summary>
public class TestEventGrainWithPartitionFiltering : EventGrain<IUserEvent, UserProjectionWithVersion>, ITestEventGrainWithPartitionFilteringGrain
{
    public TestEventGrainWithPartitionFiltering(IEventStorage eventStorage) : base(eventStorage) { }

    public Task<UserProjectionWithVersion> GetProjectionAsync() => Task.FromResult(Projection);

    public Task SendEventsAsync(params IUserEvent[] events) => ProcessEventsAsync(events);

    static TestEventGrainWithPartitionFiltering()
    {
        Configure<TestEventGrainWithPartitionFiltering>(builder =>
        {
            // Only configure handler for UserCreated, not UserDeactivated
            builder.AddPartition<UserCreated>()
                .Handle<UserCreated>((evt, projection) =>
                {
                    return projection with
                    {
                        Name = evt.Name,
                        Email = evt.Email,
                        Version = projection.Version + 1
                    };
                });
        });
    }
}

// Extended projection for testing handler validation
[Immutable, GenerateSerializer]
public record UserProjectionWithVersion(
    string Name,
    string Email,
    int TotalMessagesCount,
    int Version,
    bool IsDeactivated,
    bool HandlerValidationPassed) : IEventProjection<UserProjectionWithVersion>
{
    public static UserProjectionWithVersion CreateDefault() => new(
        Name: "Default",
        Email: "",
        TotalMessagesCount: 0,
        Version: 0,
        IsDeactivated: false,
        HandlerValidationPassed: false);

    public UserProjectionWithVersion WithVersion(int version) => this with { Version = version };
}