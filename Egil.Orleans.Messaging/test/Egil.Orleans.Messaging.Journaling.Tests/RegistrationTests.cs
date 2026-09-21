using Orleans.Journaling;

namespace Egil.Orleans.Messaging.Journaling.Tests;

public sealed class RegistrationTests(JournalingPrototypeFixture fixture) : IClassFixture<JournalingPrototypeFixture>
{
    [Fact]
    public async Task Arbitrary_names_and_payload_types_recover_independently_in_one_commit()
    {
        var grain = fixture.NewNamedGrain();
        await grain.WriteAsync();
        await grain.DeactivateAsync();
        Assert.Equal("first/second/42", await grain.ReadAsync());
    }
}

public interface INamedComponentsGrain : IGrainWithGuidKey
{
    Task WriteAsync();
    Task DeactivateAsync();
    Task<string> ReadAsync();
}

public sealed class NamedComponentsGrain(
    [FromKeyedServices("first")] IDurableOutbox<string> first,
    [FromKeyedServices("second")] IDurableOutbox<string> second,
    [FromKeyedServices("numbers")] IDurableOutbox<int> numbers,
    [FromKeyedServices("receiver")] IDurableMessageTracker tracker) : DurableGrain, INamedComponentsGrain
{
    public async Task WriteAsync()
    {
        first.Add("first");
        second.Add("second");
        numbers.Add(42);
        tracker.Evict(DateTimeOffset.MinValue);
        await WriteStateAsync();
    }

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    public Task<string> ReadAsync() => Task.FromResult($"{first[0]}/{second[0]}/{numbers[0]}");
}
