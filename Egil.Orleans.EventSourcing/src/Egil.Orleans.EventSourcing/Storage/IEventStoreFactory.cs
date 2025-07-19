namespace Egil.Orleans.EventSourcing.Storage;

public interface IEventStoreFactory
{
    IEventStore<TProjection> CreateEventStore<TProjection>(IServiceProvider serviceProvider) where TProjection : notnull, IEventProjection<TProjection>;
}
