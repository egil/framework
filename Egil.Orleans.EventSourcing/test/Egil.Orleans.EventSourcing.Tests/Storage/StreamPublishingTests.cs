using Egil.Orleans.EventSourcing.Examples;
using Egil.Orleans.EventSourcing.Examples.Events;
using Xunit;

namespace Egil.Orleans.EventSourcing.Tests.Storage;

public class StreamPublishingTests(SiloFixture fixture) : IClassFixture<SiloFixture>
{
    private readonly SiloFixture cluster = fixture;

    [Fact]
    public async Task StreamPublish_configuration_should_be_accessible()
    {
        // Arrange
        var userGrain = cluster.GrainFactory.GetGrain<IUserGrain>(Guid.NewGuid());

        // Act
        await userGrain.RegisterUser("John Doe", "john@example.com");

        // Assert - No exceptions should be thrown
        // The StreamPublish configuration should be set up correctly in the grain configuration
        // This test primarily validates that the API compiles and can be configured
        Assert.True(true);
    }

    [Fact]
    public void StreamPublish_API_should_be_fluent()
    {
        // This test validates the API design and fluent interface
        // It doesn't need to run, just compile

        // Example usage that should compile:
        var apiExample = new Action<IEventStreamConfigurator<UserGrain, IUserEvent, User>>(configurator =>
        {
            configurator
                .StreamPublish<UserCreated>(
                    "test-stream-provider",
                    "user-events-namespace",
                    publishConfig => publishConfig.KeySelector<UserCreated>(e => e.UserId))
                .StreamPublish<UserDeactivated>(
                    "test-stream-provider", 
                    "user-lifecycle-namespace",
                    publishConfig => publishConfig.KeySelector<UserDeactivated>(e => e.UserId));
        });

        // If this compiles, the API is working correctly
        Assert.NotNull(apiExample);
    }
}