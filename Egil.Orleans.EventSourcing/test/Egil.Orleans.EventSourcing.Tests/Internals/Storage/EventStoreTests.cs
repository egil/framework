using Egil.Orleans.EventSourcing.Storage;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using System.Runtime.CompilerServices;

namespace Egil.Orleans.EventSourcing.Internals.Storage;

public class EventStoreTests(AppHostFixture fixture) : IClassFixture<AppHostFixture>
{
    private record class Projection(string StrValue, int IntValue) : IEventProjection<Projection>
    {
        public static Projection CreateDefault()
            => new(string.Empty, 0);
    }

    private static GrainId RandomGrainId([CallerMemberName] string memberName = "")
        => GrainId.Create(GrainType.Create("TestEventGrain"), IdSpan.Create($"{memberName}-{Guid.NewGuid().ToString("N")[0..8]}"));

    private async Task<EventStore> CreateSut()
        => new EventStore(
            await fixture.GetTableClientAsync(),
            new SystemTextJsonGrainStorageSerializer(),
            Options.Create(new ClusterOptions() { ClusterId = "TestCluster" }));

    [Fact]
    public async Task Read_projection_from_empty_event_store()
    {
        var grainId = RandomGrainId();
        var sut = await CreateSut();

        ProjectionEntry<Projection> projection = await sut.LoadProjectionAsync<Projection>(grainId, TestContext.Current.CancellationToken);

        Assert.Equal(ProjectionEntry<Projection>.CreateDefault(), projection);
    }

    [Fact]
    public async Task Read_events_from_empty_store()
    {
        var grainId = RandomGrainId();
        var sut = await CreateSut();

         projection = await sut.LoadEventsAsync(grainId, TestContext.Current.CancellationToken);

        Assert.Equal(ProjectionEntry<Projection>.CreateDefault(), projection);
    }

}