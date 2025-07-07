using Egil.Orleans.EventSourcing.Examples.Events;

namespace Egil.Orleans.EventSourcing.Examples.EventReactors;

public class UserWelcomeEventPublisher(IEmailServer emailServer) : IEventReactor<UserWelcomeEvent, User>
{
    public async ValueTask ReactAsync(IEnumerable<UserWelcomeEvent> @event, User projection, IEventReactContext context)
    {
        foreach (var welcomeEvent in @event)
        {
            var message = $"Welcome {welcomeEvent.Name} to our service!";
            await emailServer.SendEmail(welcomeEvent.Email, message);
        }
    }
}