namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxPostmanServiceCollectionExtensionsTests
{
    [Fact]
    public void AddOutboxPostman_with_attribute_registers_all_postman_contracts_as_keyed_scoped()
    {
        var services = new ServiceCollection();

        services.AddOutboxPostman<MultiMessagePostman>();

        Assert.Contains(services, service =>
            service.ServiceType == typeof(IPostman<TestMessage>)
            && Equals(service.ServiceKey, "multi")
            && service.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, service =>
            service.ServiceType == typeof(IPostman<OtherMessage>)
            && Equals(service.ServiceKey, "multi")
            && service.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddOutboxPostman_registers_one_scoped_concrete_instance_for_multiple_contracts()
    {
        var services = new ServiceCollection();
        services.AddOutboxPostman<MultiMessagePostman>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var testPostman = scope.ServiceProvider.GetRequiredKeyedService<IPostman<TestMessage>>("multi");
        var otherPostman = scope.ServiceProvider.GetRequiredKeyedService<IPostman<OtherMessage>>("multi");

        Assert.Same(testPostman, otherPostman);
    }

    [Fact]
    public void AddOutboxPostman_with_explicit_name_does_not_require_attribute()
    {
        var services = new ServiceCollection();

        services.AddOutboxPostman<TestMessage, TestMessagePostman>("explicit");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var postman = scope.ServiceProvider.GetRequiredKeyedService<IPostman<TestMessage>>("explicit");

        Assert.IsType<TestMessagePostman>(postman);
    }

    [Fact]
    public void AddOutboxPostman_throws_when_attribute_name_is_missing()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentException>(() =>
            services.AddOutboxPostman<TestMessagePostman>());

        Assert.Equal("postmanType", ex.ParamName);
    }

    [Fact]
    public void AddOutboxPostman_throws_when_type_has_no_postman_contracts()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentException>(() =>
            services.AddOutboxPostman<NoPostmanContracts>("missing"));

        Assert.Equal("TPostman", ex.ParamName);
    }

    private sealed record TestMessage(string Value);

    private sealed record OtherMessage(string Value);

    [OutboxPostman("multi")]
    private sealed class MultiMessagePostman :
        IPostman<TestMessage>,
        IPostman<OtherMessage>
    {
        public ValueTask PostAsync(TestMessage message, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask PostAsync(OtherMessage message, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class TestMessagePostman : IPostman<TestMessage>
    {
        public ValueTask PostAsync(TestMessage message, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class NoPostmanContracts;
}