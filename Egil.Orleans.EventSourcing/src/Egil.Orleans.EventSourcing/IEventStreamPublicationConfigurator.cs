namespace Egil.Orleans.EventSourcing;

public interface IEventStreamPublicationConfigurator<TEventGrain, TEventBase, TProjection> where TProjection : class, IEventProjection<TProjection>
{
    IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> StreamNamespace(string streamNamespace);
    IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> StreamNamespace(Func<TEventBase, string> streamKeySelector);
    IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> StreamNamespace(Func<TEventBase, ReadOnlySpan<byte>> streamKeySelector);
    IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> StreamNamespace<TEvent>(Func<TEvent, string> streamKeySelector) where TEvent : TEventBase;
    IEventStreamNamespacePublicationConfigurator<TEventGrain, TEventBase, TProjection> StreamNamespace<TEvent>(Func<TEvent, ReadOnlySpan<byte>> streamKeySelector) where TEvent : TEventBase;
}
