namespace Egil.Orleans.EventSourcing.Reactors;

internal interface IEventReactorFactory<TProjection>
    where TProjection : notnull
{
    IEventReactor<TProjection> Create();
}
