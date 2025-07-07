using Egil.Orleans.EventSourcing.Examples;

namespace Egil.Orleans.EventSourcing.Tests;

/// <summary>
/// TDD tests for event handler registration and execution.
/// </summary>
#pragma warning disable xUnit1051 // Calls to methods which accept CancellationToken should use TestContext.Current.CancellationToken
public class EventGrainTests(SiloFixture fixture) : IClassFixture<SiloFixture>
{
    [Fact]
    public async Task Event_processing_invokes_configured_handler()
    {
        var eventStorage = fixture.EventStorage;
        var grain = fixture.GetGrain<IUserGrain>(Guid.NewGuid());

        await grain.RegisterUser("John Doe", "john.doe@example.com");

        var user = await grain.GetUser();
        Assert.NotNull(user);
        Assert.Equal("John Doe", user.Name);
        Assert.Equal("john.doe@example.com", user.Email);
    }
}
