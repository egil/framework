using Egil.Orleans.EventSourcing.Internal.EventHandlerFactories;
using Egil.Orleans.EventSourcing.Internal.EventReactorFactories;

namespace Egil.Orleans.EventSourcing.Internal;

internal interface IEventStream<TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    IEventStream<TProjection>? TryCast<TEvent>(TEvent @event) where TEvent : notnull;

    IEventHandlerFactory<TProjection>[] Handlers { get; }

    IEventReactorFactory<TProjection>[] Publishers { get; }
}
