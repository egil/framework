using Egil.Orleans.EventSourcing.Examples.Events;
using Egil.Orleans.EventSourcing.Reactors;

namespace Egil.Orleans.EventSourcing.Examples.EventReactors;

public class UserWelcomeEmailSender(IEmailServer emailServer) : IEventReactor<UserWelcomeEvent, User>
{
    public string Id { get; } = nameof(UserWelcomeEmailSender);

    public async ValueTask ReactAsync(IEnumerable<UserWelcomeEvent> @event, User projection, IEventReactContext context, CancellationToken cancellationToken = default)
    {
        foreach (var welcomeEvent in @event)
        {
            var message = $"Welcome {welcomeEvent.Name} to our service!";
            await emailServer.SendEmail(welcomeEvent.Email, message);
        }
    }
}