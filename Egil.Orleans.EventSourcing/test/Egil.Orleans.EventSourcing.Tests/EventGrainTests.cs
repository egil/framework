using Egil.Orleans.EventSourcing.Examples;

namespace Egil.Orleans.EventSourcing.Tests;

/// <summary>
/// TDD tests for event handler registration and execution.
/// </summary>
#pragma warning disable xUnit1051 // Calls to methods which accept CancellationToken should use TestContext.Current.CancellationToken
public class EventGrainTests(SiloFixture fixture) : IClassFixture<SiloFixture>
{
    private CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Event_processing_invokes_configured_handler()
    {
        // Arrange - Test that handler registration and execution works
        var eventStorage = fixture.EventStorage;
        var grain = fixture.GetGrain<IUserGrain>(Guid.NewGuid());

        // Act - Process the event
        await grain.RegisterUser("John Doe", "john.doe@example.com");   
    }
}
