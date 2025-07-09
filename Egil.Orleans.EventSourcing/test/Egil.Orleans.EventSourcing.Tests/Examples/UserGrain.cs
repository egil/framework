using Egil.Orleans.EventSourcing.EventStores;
using Egil.Orleans.EventSourcing.Examples.EventHandlers;
using Egil.Orleans.EventSourcing.Examples.Events;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;

namespace Egil.Orleans.EventSourcing.Examples;

/// <summary>
/// Example implementation of an event-sourced grain that manages user state.
/// This grain demonstrates the key concepts of event sourcing:
/// 1. State is derived from a sequence of events rather than stored directly
/// 2. Each method that modifies state creates events that are processed
/// 3. The projection (User) is rebuilt by applying events through handlers
/// 4. Events can be processed individually or in batches with different transactional scopes
/// </summary>
public sealed class UserGrain([FromKeyedServices("eventStoreProvider")] IEventStore<User> storage, TimeProvider timeProvider)
    : EventGrain<UserGrain, User>(storage), IUserGrain
{
    public async ValueTask RegisterUser(string name, string email)
    {
        var userId = this.GetPrimaryKeyString();
        AppendEvent(new UserCreated(userId, name, email, timeProvider.GetUtcNow()));
        AppendEvent(new UserWelcomeEvent(email, name, timeProvider.GetUtcNow()));
        await ProcessEventsAsync();
    }

    /// <summary>
    /// Deactivates a user by creating a single event.
    /// 
    /// KEY POINT: Uses ProcessEventAsync{TEvent} directly, which means:
    /// - This single event gets its own processing scope
    /// - The event and updated projection are saved immediately
    /// - Suitable for operations that create only one event
    /// - Each call to this method results in a separate storage transaction
    /// </summary>
    public async ValueTask<bool> Deactivate(string reason)
    {
        var userId = this.GetPrimaryKeyString();
        var @event = new UserDeactivated(userId, reason, timeProvider.GetUtcNow());

        // Direct call to ProcessEventAsync<TEvent> - creates its own scope
        await ProcessEventAsync(@event);

        // Return the current state after processing the event
        return Projection.IsDeactivated;
    }

    /// <summary>
    /// Sends multiple messages by creating multiple events in a SINGLE processing scope.
    /// 
    /// KEY POINT: Like RegisterUser, this uses ProcessEventAsync(Func{Task}) to batch
    /// multiple events together. Benefits:
    /// - All message events are saved in one atomic transaction
    /// - Better performance than saving each message individually
    /// - Ensures all-or-nothing semantics for the batch
    /// - The projection is updated once with the cumulative effect of all messages
    /// </summary>
    public async ValueTask SendMessage(ImmutableArray<string> messages)
    {
        var userId = this.GetPrimaryKeyString();

        // All these events share the same processing scope
        // They accumulate in the context and are saved together
        foreach (var message in messages)
        {
            var @event = new UserMessageSent(userId, message, timeProvider.GetUtcNow());
            AppendEvent(@event);
        }

        await ProcessEventsAsync();
    }

    /// <summary>
    /// Returns the current projection (materialized view) of the user.
    /// The projection is the current state derived from all processed events.
    /// </summary>
    public ValueTask<User> GetUser() => ValueTask.FromResult(Projection);

    /// <summary>
    /// Retrieves messages from the event stream, demonstrating how to query
    /// the event history. This uses the grain's event stream access to read
    /// specific event types.
    /// </summary>
    public async IAsyncEnumerable<UserMessage> GetLatestMessages()
    {
        // GetEventsAsync reads events of a specific type from the appropriate stream
        // This allows querying historical events without maintaining separate state
        await foreach (var evt in GetEventsAsync<UserMessageSent>())
        {
            yield return new UserMessage(evt.UserId, evt.Message, evt.Timestamp);
        }
    }

    /// <summary>
    /// Event handler for UserCreated events. This is a pure function that takes
    /// an event and the current projection, and returns an updated projection.
    /// This pattern ensures that state changes are predictable and testable.
    /// </summary>
    private static User ApplyUserCreated(UserCreated @event, User projection)
    {
        // Create a new projection with updated values from the event
        // Using 'with' expressions maintains immutability
        return projection with
        {
            UserId = @event.UserId,
            Name = @event.Name,
            Email = @event.Email,
            CreatedAt = @event.Timestamp,
            LastModifiedAt = @event.Timestamp
        };
    }

    /// <summary>
    /// Event handler for UserDeactivated events. Note that this handler has access
    /// to the grain instance, allowing it to use injected dependencies like TimeProvider.
    /// </summary>
    private User ApplyUserDeactivated(UserDeactivated @event, User projection)
    {
        return projection with
        {
            IsDeactivated = true,
            LastModifiedAt = timeProvider.GetUtcNow(),
        };
    }

    /// <summary>
    /// Configures the event streams and handlers for this grain.
    /// This is where the event sourcing infrastructure is set up:
    /// - Define which events belong to which streams
    /// - Register handlers that process events to update projections
    /// - Configure retention policies for events
    /// - Set up event reactors for side effects
    /// </summary>
    protected override void Configure(IEventStoreConfigurator<UserGrain, User> builder)
    {
        // Configure a stream for user-related events (UserCreated, UserDeactivated)
        // A stream groups related events and can have its own retention policies
        builder.AddStream<IUserEvent>()
            // Register a handler for UserCreated events using a static method reference
            .Handle<UserCreated>(ApplyUserCreated)

            // Register a handler for UserDeactivated using an instance method
            // The lambda receives the grain instance, allowing access to dependencies
            .Handle<UserDeactivated>(static grain => grain.ApplyUserDeactivated)

            // Register a generic handler that runs for all IUserEvent events
            // This demonstrates how to track metrics across all events in a stream
            .Handle((@event, user) => user with { EventsCount = user.EventsCount + 1 });

        // Configure a separate stream for message events
        // Different streams can have different retention policies and handlers
        builder.AddStream<UserMessageSent>()
            // Keep events for 7 days based on their timestamp
            // This demonstrates time-based retention for event history
            .KeepUntil(maxAge: TimeSpan.FromDays(7), e => e.Timestamp)

            // Register a handler using dependency injection
            // The handler will be resolved from the service container
            .Handle<UserMessageReceivedHandler>()

            // Register an inline handler that updates message statistics
            // This shows how projections can maintain derived state from events
            .Handle<UserMessageSent>(static (e, projection) =>
            {
                projection = projection with
                {
                    TotalMessagesCount = projection.TotalMessagesCount + 1,
                    LastModifiedAt = e.Timestamp
                };
                return projection;
            });

        // Configure a stream for outbox pattern events
        // The outbox pattern ensures reliable event publishing to external systems
        builder.AddStream<IUserOutboxEvent>()
            // Keep events until they've been successfully processed by all reactors
            // This ensures at-least-once delivery for published events
            .KeepUntilProcessed()

            // Track the number of outbox events
            .Handle((@event, user) => user with { OutboxEventsCount = user.OutboxEventsCount + 1 });

        // Register a reactor that sends welcome emails
        // Reactors handle side effects AFTER events are successfully saved
        //.React<UserWelcomeEvent, UserWelcomeEmailSender>()

        // Configure publishing to Orleans streams
        // This enables other grains to subscribe to offensive language events
        //.StreamPublish<OffensiveLanguageDetectedEvent>(
        //    "stream-provider-name",
        //    "offensive-words-namespace",
        //    publishConfig => publishConfig.KeySelector(e => e.UserId));
    }
}
