using Egil.Orleans.EventSourcing.EventStores;
using Egil.Orleans.EventSourcing.Examples.Events;
using Egil.Orleans.EventSourcing.Reactors;

namespace Egil.Orleans.EventSourcing.Examples.EventReactors;

public class UserWelcomeEmailSender(IEmailServer emailServer) : IEventReactor<UserWelcomeEvent, User>
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