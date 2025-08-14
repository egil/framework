using Egil.Orleans.EventSourcing.Configurations;

namespace Egil.Orleans.EventSourcing.Reactors;

/// <summary>
/// Factory for creating stream publishing reactors.
/// </summary>
internal class StreamPublishingReactorFactory<TEvent, TProjection> : IEventReactorFactory<TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly IServiceProvider serviceProvider;
    private readonly StreamPublicationConfiguration publication;
    private readonly string reactorId;

    public StreamPublishingReactorFactory(
        IServiceProvider serviceProvider,
        StreamPublicationConfiguration publication,
        string reactorId)
    {
        this.serviceProvider = serviceProvider;
        this.publication = publication;
        this.reactorId = reactorId;
    }

    public IEventReactor<TProjection> Create()
    {
        var typedReactor = new StreamPublishingReactor<TEvent, TProjection>(serviceProvider, publication, reactorId);
        return new EventReactorWrapper<TEvent, TProjection>(typedReactor, reactorId);
    }
}