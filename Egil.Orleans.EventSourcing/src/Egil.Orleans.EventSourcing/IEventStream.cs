using Azure;
using Egil.Orleans.EventSourcing.Handlers;
using Egil.Orleans.EventSourcing.Reactors;
using Orleans.Storage;
using System.Collections.Immutable;

namespace Egil.Orleans.EventSourcing;

internal interface IEventStream<TProjection> where TProjection : notnull, IEventProjection<TProjection>
{
    string Name { get; }

    bool Matches<TEvent>(TEvent? @event) where TEvent : notnull;

    IEventEntry CreateEventEntry<TEvent>(TEvent @event, long sequenceNumber)
        where TEvent : notnull;

    IEventEntry CreateEventEntry(IGrainStorageSerializer serializer, byte[] binaryData, long sequenceNumber, ImmutableDictionary<string, ReactorState> reactorStatus, DateTimeOffset? timestamp, ETag etag);

    byte[] SerializeEvent(IGrainStorageSerializer serializer, IEventEntry eventEntry);

    ValueTask<TProjection> ApplyEventsAsync<TEvent>(TEvent @event, TProjection projection, IEventHandlerContext context, CancellationToken cancellationToken = default)
        where TEvent : notnull;

    ValueTask<ImmutableArray<IEventEntry>> ReactEventsAsync(ImmutableArray<IEventEntry> events, TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default);
}
