namespace Egil.Orleans.EventSourcing;

public interface IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection>
    where TEventBase : notnull
    where TProjection : notnull
{
    IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> Key(string key);
    IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> Key(Guid key);
    IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> Key(long key);
    IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> Key(ReadOnlySpan<byte> key);
    IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> KeySelector(Func<TEventBase, string> streamKeySelector);
    IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> KeySelector(Func<TEventBase, Guid> streamKeySelector);
    IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> KeySelector(Func<TEventBase, long> streamKeySelector);
    IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> KeySelector(Func<TEventBase, ReadOnlySpan<byte>> streamKeySelector);
    IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> KeySelector<TEvent>(Func<TEvent, string> streamKeySelector) where TEvent : TEventBase;
    IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> KeySelector<TEvent>(Func<TEvent, Guid> streamKeySelector) where TEvent : TEventBase;
    IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> KeySelector<TEvent>(Func<TEvent, long> streamKeySelector) where TEvent : TEventBase;
    IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> KeySelector<TEvent>(Func<TEvent, ReadOnlySpan<byte>> streamKeySelector) where TEvent : TEventBase;
}