# EventGrain Public API Usage Guide

This document demonstrates how to use the new `EventGrain<TState>` base class for event sourcing in Orleans with multi-stream support and outbox pattern.

## Key Features

- **Multi-Stream Support**: Each grain can have multiple named event streams with independent configurations
- **Configurable Deduplication**: Per-stream deduplication by event ID
- **Flexible Retention Policies**: Time-based, count-based, or latest-per-ID retention
- **Outbox Pattern**: Reliable event publishing with retry logic
- **Automatic Recovery**: State rebuilding from event log when projection is missing/corrupted
- **Orleans Best Practices**: Proper grain lifecycle management and migration support

## Basic Usage

### 1. Define Your Events

```csharp
// Events can implement interfaces for additional behavior
public record UserRegistered(string UserId, string Email, DateTime Timestamp) : IDeduplicatedEvent
{
    public string DeduplicationId => UserId;
}

public record UserEmailChanged(string UserId, string NewEmail, DateTime Timestamp) : IDeduplicatedEvent
{
    public string DeduplicationId => UserId;
}

// Derived events for outbox
public record WelcomeEmailRequested(string UserId, string Email) : IPublishableEvent, ITargetedEvent
{
    public string TargetStream => "email-notifications";
}
```

### 2. Define Your State

```csharp
public class UserState
{
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string Status { get; set; } = "Active";
    public DateTime LastUpdated { get; set; }
}
```

### 3. Implement Your Grain

```csharp
public class UserGrain : EventGrain<UserState>, IUserGrain
{
    public UserGrain(
        [PersistentState("projection")] IPersistentState<ProjectionState<UserState>> projectionState,
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

    // Configure event streams
    protected override IReadOnlyDictionary<Type, EventStreamConfiguration> ConfigureEventStreams()
    {
        return new Dictionary<Type, EventStreamConfiguration>
        {
            [typeof(UserRegistered)] = new EventStreamConfiguration
            {
                StreamName = "user-lifecycle",
                EnableDeduplicationById = true,
                GetEventId = evt => ((UserRegistered)evt).DeduplicationId,
                RetentionPolicy = EventRetentionPolicy.KeepLatestPerDeduplicationKey()
            },
            [typeof(UserEmailChanged)] = new EventStreamConfiguration
            {
                StreamName = "user-profile",
                EnableDeduplicationById = true,
                GetEventId = evt => ((UserEmailChanged)evt).DeduplicationId,
                RetentionPolicy = EventRetentionPolicy.KeepRecent(TimeSpan.FromDays(30))
            }
        };
    }

    // Apply events to state
    protected override void ApplyEvent(UserState state, object @event)
    {
        switch (@event)
        {
            case UserRegistered registered:
                state.UserId = registered.UserId;
                state.Email = registered.Email;
                state.LastUpdated = registered.Timestamp;
                break;
            case UserEmailChanged emailChanged:
                state.Email = emailChanged.NewEmail;
                state.LastUpdated = emailChanged.Timestamp;
                break;
        }
    }

    // Create derived events for outbox
    protected override IEnumerable<object> CreateDerivedEvents(object appliedEvent, UserState newState)
    {
        return appliedEvent switch
        {
            UserRegistered registered => new object[]
            {
                new WelcomeEmailRequested(registered.UserId, registered.Email)
            },
            _ => Enumerable.Empty<object>()
        };
    }
}
```

## Configuration Options

### Event Stream Configuration

```csharp
new EventStreamConfiguration
{
    StreamName = "my-stream",
    EnableDeduplicationById = true,
    GetEventId = evt => ((MyEvent)evt).SomeProperty,
    RetentionPolicy = EventRetentionPolicy.KeepRecent(TimeSpan.FromDays(10)),
    ShouldStoreEvent = evt => ((MyEvent)evt).ShouldPersist
}
```

### Retention Policies

```csharp
// Keep all events
EventRetentionPolicy.KeepAll()

// Keep events newer than 10 days
EventRetentionPolicy.KeepRecent(TimeSpan.FromDays(10))

// Keep only latest 100 events
EventRetentionPolicy.KeepLatest(100)

// Keep only latest event per deduplication ID
EventRetentionPolicy.KeepLatestPerDeduplicationKey()
```

## Event Processing Flow

1. **Event Ingestion**: Events received via `ProcessEventAsync()` or Orleans streams
2. **Storage**: Events persisted to configured storage with deduplication/retention
3. **Quick Response**: Caller receives immediate acknowledgment
4. **Async Processing**: Event applied to projection state in background
5. **Outbox Creation**: Derived events added to outbox
6. **State Persistence**: Updated projection saved to storage
7. **Event Publishing**: Outbox events published with retry logic

## Recovery and Migration

The `EventGrain` handles:
- **Automatic Recovery**: Rebuilds state from events if projection is missing
- **Incremental Catch-up**: Applies only missing events when projection exists
- **Grain Migration**: Seamless movement between silos without data loss
- **Outbox Recovery**: Processes pending outbox events on activation

## Stream Integration

To receive events from Orleans streams:

```csharp
// The grain automatically implements IAsyncObserver<object>
// Subscribe to streams in OnActivateAsync:
public override async Task OnActivateAsync(CancellationToken cancellationToken)
{
    await base.OnActivateAsync(cancellationToken);
    
    var streamProvider = this.GetStreamProvider("MyProvider");
    var stream = streamProvider.GetStream<object>("my-stream", this.GetPrimaryKey());
    await stream.SubscribeAsync(this);
}
```

## Best Practices

1. **Keep Events Immutable**: Use records for event types
2. **Implement Deduplication Interfaces**: Use `IDeduplicatedEvent` for events that need deduplication
3. **Use Targeted Events**: Implement `ITargetedEvent` for explicit stream routing
4. **Handle Serialization**: Ensure events and state are serializable
5. **Monitor Outbox**: Set up monitoring for outbox processing failures
6. **Configure Retention**: Choose appropriate retention policies for each stream
7. **Test Recovery**: Verify state rebuilding works correctly for your domain

## Error Handling

The EventGrain provides automatic retry for:
- Projection state persistence failures
- Outbox event publishing failures
- Transient storage errors

Failed operations are logged and retried according to configuration.
