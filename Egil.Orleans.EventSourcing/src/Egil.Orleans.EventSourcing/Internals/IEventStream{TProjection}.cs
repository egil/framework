using Egil.Orleans.EventSourcing.Internals.EventHandlerFactories;
using Egil.Orleans.EventSourcing.Internals.EventReactorFactories;

namespace Egil.Orleans.EventSourcing.Internals;

internal interface IEventStream<TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    string Name { get; }

    bool Matches<TEvent>(TEvent? @event) where TEvent : notnull;

    IEventHandlerFactory<TProjection>[] Handlers { get; }

    IEventReactorFactory<TProjection>[] Publishers { get; }
}
