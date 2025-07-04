using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing.Examples;

// Immutable events
public sealed record UserRegistered(string UserId, string Email, DateTime Timestamp);
public sealed record UserEmailChanged(string UserId, string NewEmail, DateTime Timestamp);
public sealed record UserStatusUpdated(string UserId, string Status, DateTime Timestamp);

// Immutable derived events for outbox
public sealed record WelcomeEmailRequested(string UserId, string Email);
public sealed record UserAnalyticsEvent(string UserId, string EventType, DateTime Timestamp);

// Immutable projection state
public sealed record UserProjection(
    string UserId,
    string Email,
    string Status,
    DateTime LastUpdated,
    int EventCount)
{
    public static UserProjection Empty => new("", "", "Active", DateTime.MinValue, 0);
}

// Strongly-typed event handlers using the new API
public sealed class UserRegisteredHandler : EventHandler<UserRegistered, UserProjection>
{
    public override EventApplicationResult<UserProjection> ApplyEvent(UserProjection projection, UserRegistered @event)
    {
        var updatedProjection = projection with
        {
            UserId = @event.UserId,
            Email = @event.Email,
            LastUpdated = @event.Timestamp,
            EventCount = projection.EventCount + 1
        };
        
        var outboxEvents = new[]
        {
            CreateOutboxEvent(new WelcomeEmailRequested(@event.UserId, @event.Email), @event.UserId, "email-notifications"),
            CreateOutboxEvent(new UserAnalyticsEvent(@event.UserId, "UserRegistered", @event.Timestamp), @event.UserId, "analytics")
        };
        
        return UpdateProjection(updatedProjection, outboxEvents);
    }
}

public sealed class UserEmailChangedHandler : EventHandler<UserEmailChanged, UserProjection>
{
    public override EventApplicationResult<UserProjection> ApplyEvent(UserProjection projection, UserEmailChanged @event)
    {
        var updatedProjection = projection with
        {
            Email = @event.NewEmail,
            LastUpdated = @event.Timestamp,
            EventCount = projection.EventCount + 1
        };
        
        var outboxEvent = CreateOutboxEvent(
            new UserAnalyticsEvent(@event.UserId, "EmailChanged", @event.Timestamp), 
            @event.UserId, 
            "analytics");
        
        return UpdateProjection(updatedProjection, outboxEvent);
    }
}

public sealed class UserStatusUpdatedHandler : EventHandler<UserStatusUpdated, UserProjection>
{
    public override EventApplicationResult<UserProjection> ApplyEvent(UserProjection projection, UserStatusUpdated @event)
    {
        var updatedProjection = projection with
        {
            Status = @event.Status,
            LastUpdated = @event.Timestamp,
            EventCount = projection.EventCount + 1
        };
        
        var outboxEvent = CreateOutboxEvent(
            new UserAnalyticsEvent(@event.UserId, "StatusUpdated", @event.Timestamp), 
            @event.UserId, 
            "analytics");
        
        return UpdateProjection(updatedProjection, outboxEvent);
    }
}

// Example grain interface
public interface IUserGrain : IEventGrain
{
    Task RegisterUser(string email);
    Task ChangeEmail(string newEmail);
    Task UpdateStatus(string status);
    Task<UserProjection> GetCurrentProjection();
}

// Example grain implementation using the new builder-based configuration
public sealed class UserGrain : EventGrain<UserProjection>, IUserGrain
{
    public UserGrain(
        [PersistentState("projection")] IPersistentState<ProjectionState<UserProjection>> projectionState,
        IEventStorage eventStorage,
        IOutboxStorage outboxStorage,
        IEventPublisher eventPublisher,
        ILogger<UserGrain> logger)
        : base(projectionState, eventStorage, outboxStorage, eventPublisher, logger)
    {
    }

    // Business methods
    public async Task RegisterUser(string email)
    {
        var userId = this.GetPrimaryKeyString();
        var @event = new UserRegistered(userId, email, DateTime.UtcNow);
        await ProcessEventAsync(@event);
    }

    public async Task ChangeEmail(string newEmail)
    {
        var userId = this.GetPrimaryKeyString();
        var @event = new UserEmailChanged(userId, newEmail, DateTime.UtcNow);
        await ProcessEventAsync(@event);
    }

    public async Task UpdateStatus(string status)
    {
        var userId = this.GetPrimaryKeyString();
        var @event = new UserStatusUpdated(userId, status, DateTime.UtcNow);
        await ProcessEventAsync(@event);
    }

    public Task<UserProjection> GetCurrentProjection() => Task.FromResult(Projection);

    // Event sourcing configuration using the new builder API
    protected override void ConfigureEventStreams(EventStreamBuilder<UserProjection> builder)
    {
        builder
            .ForEvent<UserRegistered>()
            .ToStream("user-lifecycle")
            .WithDeduplication(evt => evt.UserId)
            .WithRetention(EventRetentionPolicies.KeepLatestPerDeduplicationKey())
            .HandledBy(new UserRegisteredHandler());

        builder
            .ForEvent<UserEmailChanged>()
            .ToStream("user-profile")
            .WithDeduplication(evt => evt.UserId)
            .WithRetention(EventRetentionPolicies.KeepLatestPerDeduplicationKey())
            .HandledBy(new UserEmailChangedHandler());

        builder
            .ForEvent<UserStatusUpdated>()
            .ToStream("user-status")
            .WithRetention(EventRetentionPolicies.KeepRecent(TimeSpan.FromDays(30)))
            .HandledBy(new UserStatusUpdatedHandler());
    }

    protected override UserProjection CreateInitialProjection() => UserProjection.Empty;

    // Implementation of abstract methods
    public override async Task ProcessEventAsync(object @event, CancellationToken cancellationToken = default)
    {
        var result = await ProcessEventWithHandlerAsync(@event, cancellationToken);
        // The base class would handle persistence and outbox processing
    }
}

// Example showing functional event handlers (alternative approach)
public sealed class FunctionalUserGrain : EventGrain<UserProjection>, IUserGrain
{
    public FunctionalUserGrain(
        [PersistentState("projection")] IPersistentState<ProjectionState<UserProjection>> projectionState,
        IEventStorage eventStorage,
        IOutboxStorage outboxStorage,
        IEventPublisher eventPublisher,
        ILogger<FunctionalUserGrain> logger)
        : base(projectionState, eventStorage, outboxStorage, eventPublisher, logger)
    {
    }

    // Business methods
    public async Task RegisterUser(string email)
    {
        var userId = this.GetPrimaryKeyString();
        var @event = new UserRegistered(userId, email, DateTime.UtcNow);
        await ProcessEventAsync(@event);
    }

    public async Task ChangeEmail(string newEmail)
    {
        var userId = this.GetPrimaryKeyString();
        var @event = new UserEmailChanged(userId, newEmail, DateTime.UtcNow);
        await ProcessEventAsync(@event);
    }

    public async Task UpdateStatus(string status)
    {
        var userId = this.GetPrimaryKeyString();
        var @event = new UserStatusUpdated(userId, status, DateTime.UtcNow);
        await ProcessEventAsync(@event);
    }

    public Task<UserProjection> GetCurrentProjection() => Task.FromResult(Projection);

    // Event sourcing configuration using inline functional handlers
    protected override void ConfigureEventStreams(EventStreamBuilder<UserProjection> builder)
    {
        builder
            .ForEvent<UserRegistered>()
            .ToStream("user-lifecycle")
            .WithDeduplication(evt => evt.UserId)
            .WithRetention(EventRetentionPolicies.KeepLatestPerDeduplicationKey())
            .HandledBy((projection, evt) =>
            {
                var updatedProjection = projection with
                {
                    UserId = evt.UserId,
                    Email = evt.Email,
                    LastUpdated = evt.Timestamp,
                    EventCount = projection.EventCount + 1
                };

                var outbox = new Outbox(new[]
                {
                    new OutboxEvent(
                        Id: Guid.NewGuid().ToString(),
                        GrainId: evt.UserId,
                        Event: new WelcomeEmailRequested(evt.UserId, evt.Email),
                        CreatedAt: DateTime.UtcNow,
                        EventTypeName: nameof(WelcomeEmailRequested),
                        TargetStream: "email-notifications")
                });

                return new EventApplicationResult<UserProjection>(updatedProjection, outbox);
            });

        builder
            .ForEvent<UserEmailChanged>()
            .ToStream("user-profile")
            .WithDeduplication(evt => evt.UserId)
            .WithRetention(EventRetentionPolicies.KeepLatestPerDeduplicationKey())
            .HandledBy((projection, evt) => projection with
            {
                Email = evt.NewEmail,
                LastUpdated = evt.Timestamp,
                EventCount = projection.EventCount + 1
            });

        builder
            .ForEvent<UserStatusUpdated>()
            .ToStream("user-status")
            .WithRetention(EventRetentionPolicies.KeepRecent(TimeSpan.FromDays(30)))
            .HandledBy((projection, evt) => projection with
            {
                Status = evt.Status,
                LastUpdated = evt.Timestamp,
                EventCount = projection.EventCount + 1
            });
    }

    protected override UserProjection CreateInitialProjection() => UserProjection.Empty;

    public override async Task ProcessEventAsync(object @event, CancellationToken cancellationToken = default)
    {
        var result = await ProcessEventWithHandlerAsync(@event, cancellationToken);
        // The base class would handle persistence and outbox processing
    }
}
