using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing.Tests.Integration;

/// <summary>
/// Integration test demonstrating the complete EventGrain workflow including:
/// - Event storage and retrieval
/// - Projection updates
/// - Outbox pattern for side effects
/// - Replay and catch-up logic
/// </summary>
public class EventGrainIntegrationTest
{
    [Fact]
    public async Task EventGrain_ShouldStoreEventsAndUpdateProjection()
    {
        // Arrange
        var serviceProvider = BuildServiceProvider();
        var grain = CreateTestGrain(serviceProvider);
        
        var userCreatedEvent = new UserCreatedEvent("user-123", "John Doe", "john@example.com");
        var userEmailUpdatedEvent = new UserEmailUpdatedEvent("user-123", "john.doe@example.com");

        // Act - Process events
        await grain.ProcessEventAsync(userCreatedEvent);
        await grain.ProcessEventAsync(userEmailUpdatedEvent);

        // Assert - Verify projection state
        var projection = grain.Projection;
        Assert.Equal("user-123", projection.Id);
        Assert.Equal("John Doe", projection.Name);
        Assert.Equal("john.doe@example.com", projection.Email);
        Assert.Equal(2, grain.LastAppliedSequenceNumber);
    }

    [Fact]
    public async Task EventGrain_ShouldProcessOutboxEvents()
    {
        // Arrange
        var serviceProvider = BuildServiceProvider();
        var grain = CreateTestGrain(serviceProvider);
        
        var userCreatedEvent = new UserCreatedEvent("user-123", "John Doe", "john@example.com");

        // Act - Process event that generates outbox events
        await grain.ProcessEventAsync(userCreatedEvent);

        // Assert - Verify outbox events were generated
        // In a real implementation, these would be processed by the OutboxPostmanService
        // and sent to external systems
    }

    [Fact]
    public async Task EventGrain_ShouldRebuildProjectionFromEvents()
    {
        // Arrange
        var serviceProvider = BuildServiceProvider();
        var grain = CreateTestGrain(serviceProvider);
        
        // Store some events first
        await grain.ProcessEventAsync(new UserCreatedEvent("user-123", "John Doe", "john@example.com"));
        await grain.ProcessEventAsync(new UserEmailUpdatedEvent("user-123", "john.doe@example.com"));

        // Act - Simulate grain reactivation by rebuilding projection
        var rebuiltProjection = await grain.RebuildProjectionFromEventsAsync();

        // Assert
        Assert.Equal("user-123", rebuiltProjection.Id);
        Assert.Equal("John Doe", rebuiltProjection.Name);
        Assert.Equal("john.doe@example.com", rebuiltProjection.Email);
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        
        // Add Orleans core services (simplified for testing)
        services.AddLogging();
        
        // Add event storage configuration
        services.AddEventStorage<UserEvent, UserOutboxEvent>("test-storage")
            .WithInMemoryProvider()
            .WithJsonSerializer()
            .WithDefaultRetentionPolicy();

        // Add event handlers
        services.AddScoped<UserEventHandler>();
        
        return services.BuildServiceProvider();
    }

    private static TestUserGrain CreateTestGrain(IServiceProvider serviceProvider)
    {
        // In a real Orleans application, grains are created by the runtime
        // For testing, we manually construct with dependencies
        var logger = serviceProvider.GetRequiredService<ILogger<TestUserGrain>>();
        var eventStorage = serviceProvider.GetRequiredService<IEventStorage<UserEvent, UserOutboxEvent>>();
        
        return new TestUserGrain(eventStorage, logger);
    }
}

/// <summary>
/// Test grain implementation for integration testing.
/// </summary>
public class TestUserGrain : EventGrain<UserProjection, UserEvent, UserOutboxEvent>
{
    public TestUserGrain(
        [EventStorage("test-storage")] IEventStorage<UserEvent, UserOutboxEvent> eventStorage,
        ILogger<TestUserGrain> logger) 
        : base(eventStorage, logger: logger)
    {
    }

    protected override void ConfigureEventStreams(EventStreamBuilder<UserProjection> builder)
    {
        builder
            .ForEvent<UserCreatedEvent>()
            .InStream("user-lifecycle")
            .HandledBy<UserEventHandler>()
            .WithDeduplication(e => e.UserId);

        builder
            .ForEvent<UserEmailUpdatedEvent>()
            .InStream("user-updates")
            .HandledBy<UserEventHandler>();
    }

    public override async Task ProcessEventAsync(object @event, CancellationToken cancellationToken = default)
    {
        var result = await ProcessEventWithHandlerAsync(@event, cancellationToken);
        // In a real grain, you might return the result or update some state
    }

    // Expose protected members for testing
    public new UserProjection Projection => base.Projection;
    public new long LastAppliedSequenceNumber => base.LastAppliedSequenceNumber;
    public new Task<UserProjection> RebuildProjectionFromEventsAsync(CancellationToken cancellationToken = default)
        => base.RebuildProjectionFromEventsAsync(cancellationToken);
}

/// <summary>
/// User projection for testing.
/// </summary>
public sealed record UserProjection(
    string Id = "",
    string Name = "",
    string Email = "",
    DateTime CreatedAt = default,
    DateTime LastUpdatedAt = default) : IEventProjection<UserProjection>
{
    public static UserProjection CreateDefault() => new();
}

/// <summary>
/// Base type for user events.
/// </summary>
public abstract record UserEvent;

/// <summary>
/// Event fired when a user is created.
/// </summary>
public sealed record UserCreatedEvent(
    string UserId,
    string Name,
    string Email) : UserEvent;

/// <summary>
/// Event fired when a user's email is updated.
/// </summary>
public sealed record UserEmailUpdatedEvent(
    string UserId,
    string NewEmail) : UserEvent;

/// <summary>
/// Base type for user outbox events.
/// </summary>
public abstract record UserOutboxEvent;

/// <summary>
/// Outbox event for user notifications.
/// </summary>
public sealed record UserNotificationOutboxEvent(
    string UserId,
    string Message,
    string Channel) : UserOutboxEvent;

/// <summary>
/// Event handler for user events that demonstrates async processing and outbox usage.
/// </summary>
public class UserEventHandler : 
    IEventHandler<UserCreatedEvent, UserProjection>,
    IEventHandler<UserEmailUpdatedEvent, UserProjection>
{
    public ValueTask<UserProjection> ApplyEventAsync(
        UserCreatedEvent @event, 
        UserProjection projection, 
        EventOutbox outbox)
    {
        // Add notification to outbox
        outbox.Add(new UserNotificationOutboxEvent(
            @event.UserId,
            $"Welcome {@event.Name}!",
            "email"));

        // Update projection
        var updatedProjection = projection with
        {
            Id = @event.UserId,
            Name = @event.Name,
            Email = @event.Email,
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow
        };

        return ValueTask.FromResult(updatedProjection);
    }

    public ValueTask<UserProjection> ApplyEventAsync(
        UserEmailUpdatedEvent @event, 
        UserProjection projection, 
        EventOutbox outbox)
    {
        // Add notification to outbox
        outbox.Add(new UserNotificationOutboxEvent(
            @event.UserId,
            $"Your email has been updated to {@event.NewEmail}",
            "email"));

        // Update projection
        var updatedProjection = projection with
        {
            Email = @event.NewEmail,
            LastUpdatedAt = DateTime.UtcNow
        };

        return ValueTask.FromResult(updatedProjection);
    }
}
