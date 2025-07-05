using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Orleans;

namespace Egil.Orleans.EventSourcing.Examples;

// Event interfaces and records
[JsonDerivedType(typeof(UserCreated), "UserCreated.V1")]
[JsonDerivedType(typeof(UserDeactivated), "UserDeactivated.V1")]
[JsonDerivedType(typeof(UserMessageReceived), "UserMessageReceived.V1")]
public interface IUserEvent;

public sealed record UserCreated(string UserId, string Name, string Email, DateTimeOffset Timestamp) : IUserEvent;
public sealed record UserDeactivated(string UserId, string Reason, DateTimeOffset Timestamp) : IUserEvent;
public sealed record UserMessageReceived(string UserId, string Message, DateTimeOffset Timestamp) : IUserEvent;

[JsonDerivedType(typeof(OffensiveLanguageDetectedEvent), "OffensiveLanguageDetectedEvent.V1")]
[JsonDerivedType(typeof(UserWelcomeEvent), "UserWelcomeEvent.V1")]
public interface IUserOutboxEvent : IUserEvent;

public sealed record OffensiveLanguageDetectedEvent(string UserId, string Message, DateTimeOffset Timestamp)
    : IUserOutboxEvent;

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

[Immutable, GenerateSerializer]
public sealed record UserMessage(string UserId, string Message, DateTimeOffset Timestamp);

public interface IUserGrain : IGrainWithStringKey
{
    ValueTask RegisterUser(string name, string email);
    ValueTask<bool> Deactivate(string reason);
    ValueTask ReceiveMessage(string message);

    ValueTask<User> GetUser();
    IAsyncEnumerable<UserMessage> GetLatestMessages();
}

public sealed class UserGrain([FromKeyedServices("user-events")] IEventStorage storage) : EventGrain<IUserEvent, User>(storage), IUserGrain
{
    // Static configuration block - this is called once per grain type
    static UserGrain() => Configure<UserGrain>(builder =>
    {
        // A partiton can have a base type and handlers for specific events.
        builder.AddPartition<IUserEvent>()
            .Handle<UserCreated>(static grain => grain.ApplyUserCreated)
            .Handle<UserDeactivated>(static grain => grain.ApplyUserDeactivated);

        // A partiton can also have multiple handlers for the same event type.
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

        // Events in a partition can be published. Similar to a handler, but the publisher is not expected to modify the projection.
        builder.AddPartition<IUserOutboxEvent>()
            .KeepUntilProcessed()
            .Publish<UserWelcomeEvent, UserWelcomeEventPublisher>()
            .StreamPublish<OffensiveLanguageDetectedEvent>(
                "stream-provider-name",
                "offensive-words-namespace",
                config => config.KeySelector(e => e.UserId));
    });

    public async ValueTask RegisterUser(string name, string email)
    {
        var userId = this.GetPrimaryKeyString();
        var @event = new UserCreated(userId, name, email, DateTimeOffset.UtcNow);
        var @event2 = new UserWelcomeEvent(email, name, DateTimeOffset.UtcNow);
        await ProcessEventsAsync(@event, @event2);
    }

    public async ValueTask<bool> Deactivate(string reason)
    {
        var userId = this.GetPrimaryKeyString();
        var @event = new UserDeactivated(userId, reason, DateTimeOffset.UtcNow);
        await ProcessEventsAsync(@event);
        return Projection.IsDeactivated;
    }

    public async ValueTask ReceiveMessage(string message)
    {
        var userId = this.GetPrimaryKeyString();
        var @event = new UserMessageReceived(userId, message, DateTimeOffset.UtcNow);
        await ProcessEventsAsync(@event);
    }

    public ValueTask<User> GetUser() => ValueTask.FromResult(Projection);

    public async IAsyncEnumerable<UserMessage> GetLatestMessages()
    {
        await foreach (var evt in GetEventsAsync<UserMessageReceived>())
        {
            yield return new UserMessage(evt.UserId, evt.Message, evt.Timestamp);
        }
    }

    private User ApplyUserCreated(UserCreated @event, User projection)
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
            LastModifiedAt = @event.Timestamp
        };
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
    public async ValueTask PublishAsync(IEnumerable<UserWelcomeEvent> @event, User projection, IEventGrainContext context)
    {
        foreach (var welcomeEvent in @event)
        {
            var message = $"Welcome {welcomeEvent.Name} to our service!";
            await emailServer.SendEmail(welcomeEvent.Email, message);
        }
    }
}