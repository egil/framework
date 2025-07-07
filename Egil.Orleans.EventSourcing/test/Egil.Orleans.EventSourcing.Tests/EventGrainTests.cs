using Egil.Orleans.EventSourcing.Examples;

namespace Egil.Orleans.EventSourcing.Tests;

[SuppressMessage("Usage", "xUnit1051:Calls to methods which accept CancellationToken should use TestContext.Current.CancellationToken", Justification = "Adds too much noise in code. No benefit it seems?")]
public class EventGrainTests(SiloFixture fixture) : IClassFixture<SiloFixture>
{
    [Fact]
    public async Task Event_processing_invokes_configured_handler()
    {
        var grain = fixture.GetGrain<IUserGrain>(Guid.NewGuid());

        await grain.RegisterUser("John Doe", "john.doe@example.com");

        var user = await grain.GetUser();
        Assert.NotNull(user);
        Assert.Equal("John Doe", user.Name);
        Assert.Equal("john.doe@example.com", user.Email);
    }

    [Fact]
    public async Task SendMessage_creates_multiple_events_and_updates_counters()
    {
        // Arrange
        var grain = fixture.GetGrain<IUserGrain>(Guid.NewGuid());
        await grain.RegisterUser("John Doe", "john.doe@example.com");

        // Act
        await grain.SendMessage(["Hello", "World", "Test"]);

        // Assert
        var user = await grain.GetUser();
        Assert.Equal(3, user.TotalMessagesCount); // Each message increments the counter
        var messages = await grain.GetLatestMessages().ToListAsync();
        Assert.Equal(3, messages.Count);
        Assert.Contains(messages, m => m.Message == "Hello");
        Assert.Contains(messages, m => m.Message == "World");
        Assert.Contains(messages, m => m.Message == "Test");
    }
}
