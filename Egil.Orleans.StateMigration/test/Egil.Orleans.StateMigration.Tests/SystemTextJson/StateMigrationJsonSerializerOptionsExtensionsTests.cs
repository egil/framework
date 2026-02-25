using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Egil.Orleans.StateMigration.Tests.SystemTextJson;

public sealed class StateMigrationJsonSerializerOptionsExtensionsTests
{
    [Fact]
    public void Add_state_migration_support_throws_for_null_service_provider()
    {
        var options = new JsonSerializerOptions();

        Assert.Throws<ArgumentNullException>(
            () => options.AddStateMigrationSupport((IServiceProvider)null!));
    }

    [Fact]
    public void Add_state_migration_support_throws_for_conflicting_callback_service_provider()
    {
        using ServiceProvider firstProvider = new ServiceCollection().BuildServiceProvider();
        using ServiceProvider secondProvider = new ServiceCollection().BuildServiceProvider();

        var options = new JsonSerializerOptions();
        options.AddStateMigrationSupport(firstProvider);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => options.AddStateMigrationSupport(secondProvider));

        Assert.Contains("service provider", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Add_state_migration_support_allows_reusing_the_same_callback_service_provider()
    {
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();

        var options = new JsonSerializerOptions();
        options.AddStateMigrationSupport(provider);

        JsonSerializerOptions result = options.AddStateMigrationSupport(provider);

        Assert.Same(options, result);
    }

    [Fact]
    public void Add_state_migration_support_throws_for_whitespace_type_property_name()
    {
        var options = new JsonSerializerOptions();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => options.AddStateMigrationSupport(" "));

        Assert.Contains("Type property name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_state_migration_support_throws_for_whitespace_value_property_name()
    {
        var options = new JsonSerializerOptions();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => options.AddStateMigrationSupport("$type", " "));

        Assert.Contains("Value property name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_state_migration_support_throws_for_conflicting_type_property_name()
    {
        var options = new JsonSerializerOptions();
        options.AddStateMigrationSupport("_type");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => options.AddStateMigrationSupport("kind"));

        Assert.Contains("already configured", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_state_migration_support_throws_for_conflicting_payload_layout()
    {
        var options = new JsonSerializerOptions();
        options.AddStateMigrationSupport(StoragePayloadLayout.Enveloped);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => options.AddStateMigrationSupport(StoragePayloadLayout.Flattened));

        Assert.Contains("already configured", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_state_migration_support_throws_for_conflicting_value_property_name()
    {
        var options = new JsonSerializerOptions();
        options.AddStateMigrationSupport("$type", "$value");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => options.AddStateMigrationSupport("$type", "_value"));

        Assert.Contains("already configured", exception.Message, StringComparison.Ordinal);
    }
}
