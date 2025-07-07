using Egil.Orleans.EventSourcing.Examples.Events;

namespace Egil.Orleans.EventSourcing.Examples.EventHandlers;

public class UserMessageReceivedHandler(IBadWordsDetector badWordsDetector) : IEventHandler<UserMessageSent, User>
{
    public async ValueTask<User> HandleAsync(UserMessageSent @event, User projection, IEventHandlerContext context)
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
