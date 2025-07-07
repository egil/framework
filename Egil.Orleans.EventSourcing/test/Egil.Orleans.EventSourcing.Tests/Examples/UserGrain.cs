using Egil.Orleans.EventSourcing.Examples.EventHandlers;
using Egil.Orleans.EventSourcing.Examples.Events;

namespace Egil.Orleans.EventSourcing.Examples;

public sealed class UserGrain(IEventStore storage, TimeProvider timeProvider)
    : EventGrain<UserGrain, User>(storage), IUserGrain
{

    public ValueTask RegisterUser(string name, string email) => ProcessEventAsync(async () =>
    {
        var userId = this.GetPrimaryKeyString();
        var @event = new UserCreated(userId, name, email, timeProvider.GetUtcNow());
        await ProcessEventAsync(@event);
        var @event2 = new UserWelcomeEvent(email, name, timeProvider.GetUtcNow());
        await ProcessEventAsync(@event2);
    });

    public async ValueTask<bool> Deactivate(string reason)
    {
        var userId = this.GetPrimaryKeyString();
        var @event = new UserDeactivated(userId, reason, timeProvider.GetUtcNow());
        await ProcessEventAsync(@event);
        return Projection.IsDeactivated;
    }

    public ValueTask SendMessage(params IEnumerable<string> messages) => ProcessEventAsync(async () =>
    {
        var userId = this.GetPrimaryKeyString();
        foreach (var message in messages)
        {
            var @event = new UserMessageSent(userId, message, timeProvider.GetUtcNow());
            await ProcessEventAsync(@event);
        }
    });

    public ValueTask<User> GetUser() => ValueTask.FromResult(Projection);

    public async IAsyncEnumerable<UserMessageSent> GetLatestMessages()
    {
        await foreach (var evt in GetEventsAsync<UserMessageSent>())
        {
            yield return new UserMessageSent(evt.UserId, evt.Message, evt.Timestamp);
        }
    }

    private static User ApplyUserCreated(UserCreated @event, User projection)
    {
        return projection with
        {
            UserId = @event.UserId,
            Name = @event.Name,
            Email = @event.Email,
            CreatedAt = @event.Timestamp,
            LastModifiedAt = @event.Timestamp
        };
    }

    private User ApplyUserDeactivated(UserDeactivated @event, User projection)
    {
        return projection with
        {
            IsDeactivated = true,
            LastModifiedAt = timeProvider.GetUtcNow(),
        };
    }

    protected override void Configure(IEventStreamBuilder<UserGrain, User> builder)
    {
        // A partition can have a base type and handlers for specific events.
        builder.AddStream<IUserEvent>()
            .Handle<UserCreated>(ApplyUserCreated)
            .Handle<UserDeactivated>(static grain => grain.ApplyUserDeactivated)
            .Handle((@event, user) => user with { EventsCount = user.EventsCount + 1 });

        // A partition can also have multiple handlers for the same event type.
        builder.AddStream<UserMessageSent>()
            .KeepUntil(TimeSpan.FromDays(7), e => e.Timestamp)
            .Handle<UserMessageReceivedHandler>()
            .Handle<UserMessageSent>(static (e, projection) =>
            {
                projection = projection with
                {
                    TotalMessagesCount = projection.TotalMessagesCount + 1,
                    LastModifiedAt = e.Timestamp
                };
                return projection;
            });

        // Events in a partition can be published. Similar to a handler, but the publisher is not expected to modify the projection.
        builder.AddStream<IUserOutboxEvent>()
            .KeepUntilProcessed()
            .Handle((@event, user) => user with { OutboxEventsCount = user.OutboxEventsCount + 1 });
        //    .Publish<UserWelcomeEvent, UserWelcomeEventPublisher>()
        //    .StreamPublish<OffensiveLanguageDetectedEvent>(
        //        "stream-provider-name",
        //        "offensive-words-namespace",
        //        publishConfig => publishConfig.KeySelector(e => e.UserId));
    }
}
