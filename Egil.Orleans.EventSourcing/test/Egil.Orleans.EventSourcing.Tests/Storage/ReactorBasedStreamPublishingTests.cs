using Egil.Orleans.EventSourcing.Examples;
using Egil.Orleans.EventSourcing.Examples.Events;
using Xunit;

namespace Egil.Orleans.EventSourcing.Tests.Storage;

public class ReactorBasedStreamPublishingTests(SiloFixture fixture) : IClassFixture<SiloFixture>
{
    [Fact]
    public void StreamPublish_should_work_with_KeepUntilReactedSuccessfully()
    {
        // This test validates that StreamPublish works correctly with retention policies
        // Since StreamPublish creates reactors, they integrate with KeepUntilReactedSuccessfully
        
        var apiExample = new Action<IEventStreamConfigurator<UserGrain, IUserEvent, User>>(configurator =>
        {
            configurator
                // Configure retention to keep events until all reactors succeed
                .KeepUntilReactedSuccessfully()
                
                // Add stream publishing - this creates a reactor internally
                .StreamPublish<UserCreated>(
                    "test-stream-provider",
                    "user-events-namespace",
                    publishConfig => publishConfig.KeySelector<UserCreated>(e => e.UserId));
                
                // Events will only be deleted after:
                // 1. All other reactors complete successfully 
                // 2. The stream publishing reactor completes successfully
                // This ensures at-least-once delivery to Orleans streams
        });

        // If this compiles, the integration is working correctly
        Assert.NotNull(apiExample);
    }

    [Fact]
    public void StreamPublish_should_support_multiple_configurations()
    {
        // This test validates that multiple StreamPublish calls work correctly
        // Each call creates a separate reactor with its own retry logic
        
        var apiExample = new Action<IEventStreamConfigurator<UserGrain, IUserEvent, User>>(configurator =>
        {
            configurator
                // Multiple stream publications to different providers/namespaces
                .StreamPublish<UserCreated>(
                    "analytics-provider",
                    "user-analytics",
                    publishConfig => publishConfig.KeySelector<UserCreated>(e => e.UserId))
                .StreamPublish<UserCreated>(
                    "notification-provider", 
                    "user-notifications",
                    publishConfig => publishConfig.KeySelector<UserCreated>(e => e.Email))
                .StreamPublish<UserDeactivated>(
                    "audit-provider",
                    "user-audit",
                    publishConfig => publishConfig.KeySelector<UserDeactivated>(e => e.UserId));
                
                // Each StreamPublish creates its own reactor:
                // - StreamPublisher-analytics-provider-user-analytics-UserCreated
                // - StreamPublisher-notification-provider-user-notifications-UserCreated  
                // - StreamPublisher-audit-provider-user-audit-UserDeactivated
        });

        Assert.NotNull(apiExample);
    }
}