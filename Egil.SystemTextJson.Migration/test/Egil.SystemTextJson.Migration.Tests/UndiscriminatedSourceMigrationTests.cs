using System.Text.Json;

namespace Egil.SystemTextJson.Migration.Tests;

public sealed class UndiscriminatedSourceMigrationTests
{
    [Fact]
    public void Deserialize_object_without_discriminator_uses_configured_static_source_migrator()
    {
        var options = CreateOptions();
        const string json = """{"firstName":"Egil","lastName":"Hansen"}""";

        UndiscriminatedStaticTarget? result = JsonSerializer.Deserialize<UndiscriminatedStaticTarget>(json, options);

        Assert.NotNull(result);
        Assert.Equal("Egil Hansen", result.Name);
    }

    [Fact]
    public void Deserialize_object_without_discriminator_uses_configured_external_source_migrator()
    {
        var options = CreateOptions(static builder => builder.RegisterMigrator<UndiscriminatedExternalMigrator>());
        const string json = """{"firstName":"Egil","lastName":"Hansen"}""";

        UndiscriminatedExternalTarget? result = JsonSerializer.Deserialize<UndiscriminatedExternalTarget>(json, options);

        Assert.NotNull(result);
        Assert.Equal("Egil Hansen", result.Name);
    }

    [Fact]
    public void Deserialize_object_without_discriminator_preserves_target_fallback_without_opt_in()
    {
        var options = CreateOptions();
        const string json = """{"value":"target","migrationPath":"target"}""";

        UndiscriminatedDefaultTarget? result = JsonSerializer.Deserialize<UndiscriminatedDefaultTarget>(json, options);

        Assert.NotNull(result);
        Assert.Equal("target", result.Value);
        Assert.Equal("target", result.MigrationPath);
    }

    [Fact]
    public void Deserialize_with_configured_source_without_matching_migrator_throws_clear_error()
    {
        var options = CreateOptions();
        const string json = """{"firstName":"Egil","lastName":"Hansen"}""";

        var exception = Assert.Throws<InvalidOperationException>(
            () => JsonSerializer.Deserialize<UndiscriminatedInvalidTarget>(json, options));

        Assert.Contains(nameof(JsonMigratableAttribute.UndiscriminatedSourceType), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(UndiscriminatedInvalidSource), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(UndiscriminatedInvalidTarget), exception.Message, StringComparison.Ordinal);
    }

    private static JsonSerializerOptions CreateOptions(Action<JsonMigrationBuilder>? configure = null)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(configure);
        return options;
    }
}

public record class UndiscriminatedStaticSource(string FirstName, string LastName);

public record class UndiscriminatedStaticOtherSource(string FirstName, string LastName);

[JsonMigratable(
    TypeDiscriminator = "undiscriminated-static-target",
    UndiscriminatedSourceType = typeof(UndiscriminatedStaticSource))]
public record class UndiscriminatedStaticTarget(string Name) :
    IMigrateFrom<UndiscriminatedStaticSource, UndiscriminatedStaticTarget>,
    IMigrateFrom<UndiscriminatedStaticOtherSource, UndiscriminatedStaticTarget>
{
    public static bool TryMigrateFrom(UndiscriminatedStaticSource source, out UndiscriminatedStaticTarget result)
    {
        result = new UndiscriminatedStaticTarget($"{source.FirstName} {source.LastName}");
        return true;
    }

    public static bool TryMigrateFrom(UndiscriminatedStaticOtherSource source, out UndiscriminatedStaticTarget result)
    {
        result = new UndiscriminatedStaticTarget($"other:{source.FirstName} {source.LastName}");
        return true;
    }
}

public record class UndiscriminatedExternalSource(string FirstName, string LastName);

[JsonMigratable(
    TypeDiscriminator = "undiscriminated-external-target",
    UndiscriminatedSourceType = typeof(UndiscriminatedExternalSource))]
public record class UndiscriminatedExternalTarget(string Name);

public sealed class UndiscriminatedExternalMigrator :
    IMigrate<UndiscriminatedExternalSource, UndiscriminatedExternalTarget>
{
    public bool TryMigrateFrom(UndiscriminatedExternalSource source, out UndiscriminatedExternalTarget result)
    {
        result = new UndiscriminatedExternalTarget($"{source.FirstName} {source.LastName}");
        return true;
    }
}

public record class UndiscriminatedDefaultSource(string Name);

[JsonMigratable(TypeDiscriminator = "undiscriminated-default-target")]
public record class UndiscriminatedDefaultTarget(string Value, string MigrationPath) :
    IMigrateFrom<UndiscriminatedDefaultSource, UndiscriminatedDefaultTarget>
{
    public static bool TryMigrateFrom(UndiscriminatedDefaultSource source, out UndiscriminatedDefaultTarget result)
    {
        result = new UndiscriminatedDefaultTarget(source.Name, "migrated");
        return true;
    }
}

public record class UndiscriminatedInvalidSource(string FirstName, string LastName);

[JsonMigratable(
    TypeDiscriminator = "undiscriminated-invalid-target",
    UndiscriminatedSourceType = typeof(UndiscriminatedInvalidSource))]
public record class UndiscriminatedInvalidTarget(string Name);

