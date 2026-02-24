using System.Text.Json;

namespace Egil.Orleans.StateMigration.Tests;

public sealed class StateMigrationJsonSerializerOptionsExtensionsTests
{
    [Fact]
    public void Add_state_migration_support_throws_for_whitespace_type_property_name()
    {
        var options = new JsonSerializerOptions();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => options.AddStateMigrationSupport(" "));

        Assert.Contains("Type property name", exception.Message, StringComparison.Ordinal);
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
}
