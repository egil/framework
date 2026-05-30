namespace Egil.Orleans.Messaging.Outboxes;

internal sealed class OutboxPostmanRegistry<TOutbox>
    where TOutbox : notnull
{
    private readonly List<OutboxPostmanRegistration<TOutbox>> postmen = [];

    public void Add<TSub>(Func<TSub, CancellationToken, ValueTask> postman)
        where TSub : TOutbox
    {
        postmen.Add(new OutboxPostmanRegistration<TOutbox>(
            item => item is TSub,
            (item, cancellationToken) => postman((TSub)item, cancellationToken),
            typeof(TSub)));
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