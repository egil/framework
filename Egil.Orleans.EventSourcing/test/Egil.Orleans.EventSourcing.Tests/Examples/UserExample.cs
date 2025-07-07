using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Egil.Orleans.EventSourcing.Examples;

// Events that share the same partition and partition rules
[JsonDerivedType(typeof(UserCreated), "UserCreated.V1")]
[JsonDerivedType(typeof(UserDeactivated), "UserDeactivated.V1")]
public interface IUserEvent;

[Immutable, GenerateSerializer, Alias("UserCreated.V1")]
public sealed record UserCreated(string UserId, string Name, string Email, DateTimeOffset Timestamp) : IUserEvent;

[Immutable, GenerateSerializer, Alias("UserDeactivated.V1")]
public sealed record UserDeactivated(string UserId, string Reason, DateTimeOffset Timestamp) : IUserEvent;

// Event that has its own partition and partition rules
[Immutable, GenerateSerializer, Alias("UserMessageReceived.V1")]
public sealed record UserMessageReceived(string UserId, string Message, DateTimeOffset Timestamp);

[JsonDerivedType(typeof(OffensiveLanguageDetectedEvent), "OffensiveLanguageDetectedEvent.V1")]
[JsonDerivedType(typeof(UserWelcomeEvent), "UserWelcomeEvent.V1")]
public interface IUserOutboxEvent : IUserEvent;

[Immutable, GenerateSerializer, Alias("OffensiveLanguageDetectedEvent.V1")]
public sealed record OffensiveLanguageDetectedEvent(string UserId, string Message, DateTimeOffset Timestamp)
    : IUserOutboxEvent;

[Immutable, GenerateSerializer, Alias("UserWelcomeEvent.V1")]
public sealed record UserWelcomeEvent(string Email, string Name, DateTimeOffset Timestamp)
    : IUserOutboxEvent;

[Immutable, GenerateSerializer, Alias("UserProjection.V1")]
public sealed record User(
    string UserId,
    string Name,
    string Email,
    int TotalMessagesCount,
    int BadWordsCount,
    bool IsDeactivated,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt) : IEventProjection<User>
{
    public static User CreateDefault() => new(
        UserId: string.Empty,
        Name: string.Empty,
        Email: string.Empty,
        TotalMessagesCount: 0,
        BadWordsCount: 0,
        IsDeactivated: false,
        CreatedAt: DateTimeOffset.MinValue,
        LastModifiedAt: DateTimeOffset.MinValue);
}

[Immutable, GenerateSerializer, Alias("UserMessage.V1")]
public sealed record UserMessage(string UserId, string Message, DateTimeOffset Timestamp);

public interface IUserGrain : IGrainWithGuidKey
{
    ValueTask RegisterUser(string name, string email);
    ValueTask<bool> Deactivate(string reason);
    ValueTask ReceiveMessage(string message);

    ValueTask<User> GetUser();
    IAsyncEnumerable<UserMessage> GetLatestMessages();
}

public sealed class UserGrain(IEventStorage storage, TimeProvider timeProvider)
    : EventGrain<UserGrain, User>(storage), IUserGrain
{
    public async ValueTask RegisterUser(string name, string email)
    {
        var userId = this.GetPrimaryKeyString();
        var @event = new UserCreated(userId, name, email, DateTimeOffset.UtcNow);
        await ProcessEventAsync(@event);
        var @event2 = new UserWelcomeEvent(email, name, DateTimeOffset.UtcNow);
        await ProcessEventAsync(@event2);
    }

    public async ValueTask<bool> Deactivate(string reason)
    {
        var userId = this.GetPrimaryKeyString();
        var @event = new UserDeactivated(userId, reason, DateTimeOffset.UtcNow);
        await ProcessEventAsync(@event);
        return Projection.IsDeactivated;
    }

    public async ValueTask ReceiveMessage(string message)
    {
        var userId = this.GetPrimaryKeyString();
        var @event = new UserMessageReceived(userId, message, DateTimeOffset.UtcNow);
        await ProcessEventAsync(@event);
    }

    public ValueTask<User> GetUser() => ValueTask.FromResult(Projection);

    public async IAsyncEnumerable<UserMessage> GetLatestMessages()
    {
        await foreach (var evt in GetEventsAsync<UserMessageReceived>())
        {
            yield return new UserMessage(evt.UserId, evt.Message, evt.Timestamp);
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

    protected override void Configure(IEventPartitionBuilder<UserGrain, User> builder)
    {
        // A partition can have a base type and handlers for specific events.
        builder.AddPartition<IUserEvent>()
            .Handle<UserCreated>(ApplyUserCreated)
            .Handle<UserDeactivated>(static grain => grain.ApplyUserDeactivated);

        // A partition can also have multiple handlers for the same event type.
        builder.AddPartition<UserMessageReceived>()
            .KeepUntil(TimeSpan.FromDays(7), e => e.Timestamp)
            .Handle<UserMessageReceivedHandler>()
            .Handle<UserMessageReceived>(static (e, projection) =>
            {
                projection = projection with
                {
                    TotalMessagesCount = projection.TotalMessagesCount + 1,
                    LastModifiedAt = e.Timestamp
                };
                return projection;
            });

        //// Events in a partition can be published. Similar to a handler, but the publisher is not expected to modify the projection.
        //builder.AddPartition<IUserOutboxEvent>()
        //    .KeepUntilProcessed()
        //    .Publish<UserWelcomeEvent, UserWelcomeEventPublisher>()
        //    .StreamPublish<OffensiveLanguageDetectedEvent>(
        //        "stream-provider-name",
        //        "offensive-words-namespace",
        //        publishConfig => publishConfig.KeySelector(e => e.UserId));
    }
}

public interface IBadWordsDetector
{
    ValueTask<ImmutableArray<string>> ExtractBadWordsAsync(string message);
}

public class UserMessageReceivedHandler(IBadWordsDetector badWordsDetector) : IEventHandler<UserMessageReceived, User>
{
    public async ValueTask<User> HandleAsync(UserMessageReceived @event, User projection, IEventGrainContext context)
    {
        var badWords = await badWordsDetector.ExtractBadWordsAsync(@event.Message);

        foreach (var badWord in badWords)
        {
            var offensiveEvent = new OffensiveLanguageDetectedEvent(@event.UserId, badWord, @event.Timestamp);
            context.AppendEvent(offensiveEvent);
        }

        return projection with
        {
            BadWordsCount = projection.BadWordsCount + badWords.Length,
            LastModifiedAt = @event.Timestamp
        };
    }
}

public interface IEmailServer
{
    ValueTask SendEmail(string email, string message);
}

public class UserWelcomeEventPublisher(IEmailServer emailServer) : IEventPublisher<UserWelcomeEvent, User>
{
    public bool CanPublish<TEvent>(TEvent @event) where TEvent : notnull => throw new NotImplementedException();

    public async ValueTask PublishAsync(IEnumerable<UserWelcomeEvent> @event, User projection, IEventGrainContext context)
    {
        foreach (var welcomeEvent in @event)
        {
            var message = $"Welcome {welcomeEvent.Name} to our service!";
            await emailServer.SendEmail(welcomeEvent.Email, message);
        }
    }
}