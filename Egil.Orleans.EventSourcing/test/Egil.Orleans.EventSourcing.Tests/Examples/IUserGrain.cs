using Egil.Orleans.EventSourcing.Examples.Events;

namespace Egil.Orleans.EventSourcing.Examples;

public interface IUserGrain : IGrainWithGuidKey
{
    ValueTask RegisterUser(string name, string email);
    ValueTask<bool> Deactivate(string reason);
    ValueTask SendMessage(params IEnumerable<string> messages);

    ValueTask<User> GetUser();
    IAsyncEnumerable<UserMessageSent> GetLatestMessages();
}
