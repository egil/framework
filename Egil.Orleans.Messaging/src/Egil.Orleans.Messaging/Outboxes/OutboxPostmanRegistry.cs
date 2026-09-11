namespace Egil.Orleans.Messaging.Outboxes;

internal sealed class OutboxPostmanRegistry<TOutbox>
    where TOutbox : notnull
{
    private readonly List<OutboxPostmanRegistration<TOutbox>> postmen = [];

    public void Add(
        Type messageType,
        Func<TOutbox, bool> matches,
        Func<TOutbox, CancellationToken, ValueTask> postman)
    {
        postmen.Add(new OutboxPostmanRegistration<TOutbox>(matches, postman, messageType));
    }

    public OutboxPostmanRegistration<TOutbox>? Find(TOutbox item)
    {
        foreach (var postman in postmen)
        {
            if (postman.ItemFilter(item))
            {
                return postman;
            }
        }

        return null;
    }
}

internal sealed record OutboxPostmanRegistration<TOutbox>(
    Func<TOutbox, bool> ItemFilter,
    Func<TOutbox, CancellationToken, ValueTask> Postman,
    Type ItemType)
    where TOutbox : notnull
{
    public ValueTask Invoke(TOutbox item, CancellationToken cancellationToken)
        => Postman(item, cancellationToken);
}