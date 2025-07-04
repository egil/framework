using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing.Examples;

#region Event Types

public abstract record UserEvent(string UserId, DateTimeOffset Timestamp);

public sealed record UserCreated(string UserId, string Name, string Email, DateTimeOffset Timestamp) 
    : UserEvent(UserId, Timestamp);

public sealed record UserEmailChanged(string UserId, string OldEmail, string NewEmail, DateTimeOffset Timestamp) 
    : UserEvent(UserId, Timestamp);

public sealed record UserDeactivated(string UserId, string Reason, DateTimeOffset Timestamp) 
    : UserEvent(UserId, Timestamp);

#endregion

#region Outbox Event Types

public abstract record UserOutboxEvent(string EventId, DateTimeOffset Timestamp);

public sealed record UserNotificationRequested(string EventId, string UserId, string Message, DateTimeOffset Timestamp) 
    : UserOutboxEvent(EventId, Timestamp);

public sealed record UserEmailUpdateRequested(string EventId, string UserId, string NewEmail, DateTimeOffset Timestamp) 
    : UserOutboxEvent(EventId, Timestamp);

#endregion

#region Projection

public sealed record UserProjection(
    string UserId,
    string Name,
    string Email,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt) : IEventProjection<UserProjection>
{
    public static UserProjection CreateDefault() => new(
        string.Empty,
        string.Empty,
        string.Empty,
        false,
        DateTimeOffset.MinValue,
        DateTimeOffset.MinValue);
}

#endregion

#region Grain Implementation with Orleans-Style Dependency Injection

/// <summary>
/// Example grain using Orleans-style attribute-based dependency injection for event storage.
/// This demonstrates the new pattern that separates serialization configuration from business logic.
/// </summary>
public class OrleansStyleUserGrain : EventGrain<UserProjection, UserEvent, UserOutboxEvent>, IGrainWithStringKey
{
    /// <summary>
    /// Constructor with Orleans persistent state and named event storage.
    /// Uses Orleans-style [EventStorage] attribute for dependency injection.
    /// </summary>
    public OrleansStyleUserGrain(
        [PersistentState("projection")] IPersistentState<ProjectionState<UserProjection>> projectionState,
        [EventStorage("user-events")] IEventStorage<UserEvent, UserOutboxEvent> eventStorage,
        ILogger<OrleansStyleUserGrain> logger)
        : base(projectionState, eventStorage, logger: logger)
    {
    }

    public override Task ProcessEventAsync(object @event, CancellationToken cancellationToken = default)
    {
        return @event switch
        {
            UserCreated userCreated => ProcessUserCreatedAsync(userCreated, cancellationToken),
            UserEmailChanged emailChanged => ProcessUserEmailChangedAsync(emailChanged, cancellationToken),
            UserDeactivated userDeactivated => ProcessUserDeactivatedAsync(userDeactivated, cancellationToken),
            _ => throw new ArgumentException($"Unknown event type: {@event.GetType()}")
        };
    }

    protected override void ConfigureEventStreams(EventStreamBuilder<UserProjection> builder)
    {
        builder
            .ForStream("domain-events")
            .OnEvent<UserCreated>()
                .HandleAsync(async (evt, projection, outbox) =>
                {
                    // Apply domain event to projection
                    var updatedProjection = projection with
                    {
                        UserId = evt.UserId,
                        Name = evt.Name,
                        Email = evt.Email,
                        IsActive = true,
                        CreatedAt = evt.Timestamp,
                        LastModifiedAt = evt.Timestamp
                    };

                    // Add outbox event for notification
                    outbox.Add(new UserNotificationRequested(
                        Guid.NewGuid().ToString(),
                        evt.UserId,
                        $"Welcome {evt.Name}!",
                        evt.Timestamp));

                    return updatedProjection;
                })
            .OnEvent<UserEmailChanged>()
                .HandleAsync(async (evt, projection, outbox) =>
                {
                    var updatedProjection = projection with
                    {
                        Email = evt.NewEmail,
                        LastModifiedAt = evt.Timestamp
                    };

                    // Add outbox event for email update
                    outbox.Add(new UserEmailUpdateRequested(
                        Guid.NewGuid().ToString(),
                        evt.UserId,
                        evt.NewEmail,
                        evt.Timestamp));

                    return updatedProjection;
                })
            .OnEvent<UserDeactivated>()
                .HandleAsync(async (evt, projection, outbox) =>
                {
                    var updatedProjection = projection with
                    {
                        IsActive = false,
                        LastModifiedAt = evt.Timestamp
                    };

                    outbox.Add(new UserNotificationRequested(
                        Guid.NewGuid().ToString(),
                        evt.UserId,
                        $"Account deactivated: {evt.Reason}",
                        evt.Timestamp));

                    return updatedProjection;
                });
    }

    #region Event Processing Methods

    private async Task ProcessUserCreatedAsync(UserCreated evt, CancellationToken cancellationToken)
    {
        // TODO: Apply event using the configured stream builder
        logger.LogInformation("Processing UserCreated event for user {UserId}", evt.UserId);
    }

    private async Task ProcessUserEmailChangedAsync(UserEmailChanged evt, CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing UserEmailChanged event for user {UserId}", evt.UserId);
    }

    private async Task ProcessUserDeactivatedAsync(UserDeactivated evt, CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing UserDeactivated event for user {UserId}", evt.UserId);
    }

    #endregion
}

/// <summary>
/// Alternative implementation using shared transaction scope (no Orleans persistent state).
/// Projection is stored alongside events in the same storage system.
/// </summary>
public class SharedScopeUserGrain : EventGrain<UserProjection, UserEvent, UserOutboxEvent>, IGrainWithStringKey
{
    /// <summary>
    /// Constructor with shared transaction scope - projection stored with events.
    /// Uses Orleans-style [EventStorage] attribute for dependency injection.
    /// </summary>
    public SharedScopeUserGrain(
        [EventStorage("user-events-shared")] IEventStorage<UserEvent, UserOutboxEvent> eventStorage,
        ILogger<SharedScopeUserGrain> logger)
        : base(eventStorage, logger: logger)
    {
    }

    public override Task ProcessEventAsync(object @event, CancellationToken cancellationToken = default)
    {
        return @event switch
        {
            UserCreated userCreated => ProcessUserCreatedAsync(userCreated, cancellationToken),
            UserEmailChanged emailChanged => ProcessUserEmailChangedAsync(emailChanged, cancellationToken),
            UserDeactivated userDeactivated => ProcessUserDeactivatedAsync(userDeactivated, cancellationToken),
            _ => throw new ArgumentException($"Unknown event type: {@event.GetType()}")
        };
    }

    protected override void ConfigureEventStreams(EventStreamBuilder<UserProjection> builder)
    {
        // Same configuration as OrleansStyleUserGrain
        builder
            .ForStream("domain-events")
            .OnEvent<UserCreated>()
                .HandleAsync(async (evt, projection, outbox) =>
                {
                    var updatedProjection = projection with
                    {
                        UserId = evt.UserId,
                        Name = evt.Name,
                        Email = evt.Email,
                        IsActive = true,
                        CreatedAt = evt.Timestamp,
                        LastModifiedAt = evt.Timestamp
                    };

                    outbox.Add(new UserNotificationRequested(
                        Guid.NewGuid().ToString(),
                        evt.UserId,
                        $"Welcome {evt.Name}!",
                        evt.Timestamp));

                    return updatedProjection;
                });
    }

    private async Task ProcessUserCreatedAsync(UserCreated evt, CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing UserCreated event for user {UserId} in shared scope", evt.UserId);
    }

    private async Task ProcessUserEmailChangedAsync(UserEmailChanged evt, CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing UserEmailChanged event for user {UserId} in shared scope", evt.UserId);
    }

    private async Task ProcessUserDeactivatedAsync(UserDeactivated evt, CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing UserDeactivated event for user {UserId} in shared scope", evt.UserId);
    }
}

#endregion
