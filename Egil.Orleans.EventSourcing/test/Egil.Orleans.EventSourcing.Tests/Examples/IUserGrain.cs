using Egil.Orleans.EventSourcing.Examples.Events;
using System.Collections.Immutable;

namespace Egil.Orleans.EventSourcing.Examples;

public interface IUserGrain : IGrainWithGuidKey
{
    ValueTask RegisterUser(string name, string email);
    ValueTask<bool> Deactivate(string reason);
    ValueTask SendMessage(ImmutableArray<string> messages);

    ValueTask<User> GetUser();
    IAsyncEnumerable<UserMessage> GetLatestMessages();
}
