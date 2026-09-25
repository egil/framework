using TimeProviderExtensions;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxProcessorServiceCollectionExtensionsTests
{
    [Fact]
    public void Processor_starts_from_silo_defaults_including_a_keyed_clock()
    {
        var pricingClock = new ManualTimeProvider();
        var builder = new FakeSiloBuilder();
        builder.Services.AddKeyedSingleton<TimeProvider>("pricing", pricingClock);
        builder.ConfigureOutboxProcessor((options, services) =>
        {
            options.TimeProvider = services.GetRequiredKeyedService<TimeProvider>("pricing");
            options.RetryDelay = TimeSpan.FromMinutes(5);
            options.KeepAlive = true;
        });

        var options = Resolve(builder, static options => options.AcknowledgePosted = static _ => { });

        Assert.Same(pricingClock, options.TimeProvider);
        Assert.Equal(TimeSpan.FromMinutes(5), options.RetryDelay);
        Assert.True(options.KeepAlive);
    }

    [Fact]
    public void Processor_configuration_overrides_silo_defaults()
    {
        var builder = new FakeSiloBuilder();
        builder.ConfigureOutboxProcessor(static options => options.RetryDelay = TimeSpan.FromMinutes(5));

        var options = Resolve(builder, static options =>
        {
            options.AcknowledgePosted = static _ => { };
            options.RetryDelay = TimeSpan.FromMinutes(10);
        });

        Assert.Equal(TimeSpan.FromMinutes(10), options.RetryDelay);
    }

    [Fact]
    public void Repeated_silo_configuration_applies_every_step()
    {
        var builder = new FakeSiloBuilder();
        builder
            .ConfigureOutboxProcessor(static options => options.Interleave = false)
            .ConfigureOutboxProcessor(static options => options.ProcessingTimeout = TimeSpan.FromSeconds(45));

        var options = Resolve(builder, static options => options.AcknowledgePosted = static _ => { });

        Assert.False(options.Interleave);
        Assert.Equal(TimeSpan.FromSeconds(45), options.ProcessingTimeout);
    }

    [Fact]
    public void Invalid_silo_default_is_rejected_when_the_processor_registers()
    {
        var builder = new FakeSiloBuilder();
        builder.ConfigureOutboxProcessor(static options => options.RetryDelay = TimeSpan.Zero);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => Resolve(builder, static options => options.AcknowledgePosted = static _ => { }));

        Assert.Equal("configure", ex.ParamName);
    }

    [Fact]
    public void Changes_after_configuration_returns_are_ignored()
    {
        OutboxProcessorOptions<string>? captured = null;

        var options = Resolve(new FakeSiloBuilder(), options =>
        {
            captured = options;
            options.AcknowledgePosted = static _ => { };
        });
        captured!.RetryDelay = TimeSpan.FromHours(1);

        Assert.Equal(TimeSpan.FromMinutes(2), options.RetryDelay);
    }

    [Fact]
    public void Library_defaults_apply_without_options_services()
    {
        var options = OutboxProcessorOptions<string>.Resolve(
            new ServiceCollection().BuildServiceProvider(),
            static options => options.AcknowledgePosted = static _ => { },
            "configure");

        Assert.Equal(TimeSpan.FromSeconds(20), options.ProcessingTimeout);
        Assert.Equal(TimeSpan.FromMinutes(2), options.RetryDelay);
        Assert.Same(TimeProvider.System, options.TimeProvider);
    }

    private static OutboxProcessorOptions<string> Resolve(
        FakeSiloBuilder builder,
        Action<OutboxProcessorOptions<string>> configure) =>
        OutboxProcessorOptions<string>.Resolve(builder.Services.BuildServiceProvider(), configure, "configure");

    private sealed class FakeSiloBuilder : ISiloBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();

        public Microsoft.Extensions.Configuration.IConfiguration Configuration { get; } =
            new Microsoft.Extensions.Configuration.ConfigurationManager();
    }
}
