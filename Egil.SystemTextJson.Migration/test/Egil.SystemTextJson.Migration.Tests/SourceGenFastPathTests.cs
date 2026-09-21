using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Tests;

/// <summary>
/// System.Text.Json only uses a source-generated type's fast-path <c>SerializeHandler</c> when the
/// options are compatible with the generated context, and any entry in <c>options.Converters</c>
/// makes them incompatible for every type. Migration support must therefore register through the
/// type-info resolver chain so that types outside migration keep the fast path.
/// </summary>
public class SourceGenFastPathTests
{
    [Fact]
    public void Type_outside_migration_keeps_fast_path_serialization()
    {
        var options = CreateSourceGenOptions();

        Assert.True(CanUseSerializeHandler(options, typeof(FastPathPlain)));
    }

    [Fact]
    public void Migratable_type_serializes_through_the_metadata_path()
    {
        // The plain contract is modified to carry the discriminator, which the generated
        // handler knows nothing about, so the migratable type itself must not use it.
        var options = CreateSourceGenOptions();

        Assert.False(CanUseSerializeHandler(options, typeof(FastPathMigratable)));
    }

    [Fact]
    public void Type_containing_a_migratable_property_still_writes_the_discriminator()
    {
        var options = CreateSourceGenOptions();

        var json = JsonSerializer.Serialize(new FastPathWrapper(new FastPathMigratable("Jane")), options);

        Assert.Equal("""{"Inner":{"$type":"fast-path","Name":"Jane"}}""", json);
        Assert.False(CanUseSerializeHandler(options, typeof(FastPathWrapper)));
    }

    [Fact]
    public void Source_generated_context_added_after_migration_support_is_used()
    {
        var options = new JsonSerializerOptions();
        options.AddJsonMigrationSupport();
        options.TypeInfoResolverChain.Add(FastPathJsonContext.Default);

        var json = JsonSerializer.Serialize(new FastPathPlain("Jane"), options);

        Assert.Equal("""{"Name":"Jane"}""", json);
        Assert.True(CanUseSerializeHandler(options, typeof(FastPathPlain)));
    }

    [Fact]
    public void Options_without_a_resolver_fall_back_to_reflection()
    {
        var options = new JsonSerializerOptions();
        options.AddJsonMigrationSupport();

        var json = JsonSerializer.Serialize(new FastPathUnregistered("Jane"), options);
        var migrated = JsonSerializer.Deserialize<FastPathMigratable>("""{"$type":"fast-path","Name":"Jane"}""", options);

        Assert.Equal("""{"Name":"Jane"}""", json);
        Assert.Equal("Jane", migrated!.Name);
    }

    [Fact]
    public void Converter_registered_before_migration_support_wins_for_the_migratable_type()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new FastPathMigratableConverter());
        options.AddJsonMigrationSupport();

        var json = JsonSerializer.Serialize(new FastPathMigratable("Jane"), options);

        Assert.Equal("\"converter\"", json);
    }

    [Fact]
    public void Converter_registered_after_migration_support_does_not_bypass_migration()
    {
        var options = new JsonSerializerOptions();
        options.AddJsonMigrationSupport();
        options.Converters.Add(new FastPathMigratableConverter());

        // The plain contract resolves to the user's converter, which cannot carry a
        // discriminator, so the configuration is refused rather than silently bypassed.
        Assert.ThrowsAny<NotSupportedException>(() => JsonSerializer.Serialize(new FastPathMigratable("Jane"), options));
    }

    private static JsonSerializerOptions CreateSourceGenOptions()
    {
        var options = new JsonSerializerOptions(FastPathJsonContext.Default.Options);
        options.AddJsonMigrationSupport();
        return options;
    }

    private static bool CanUseSerializeHandler(JsonSerializerOptions options, Type type)
    {
        // STJ decides fast-path eligibility when a contract is configured against frozen options;
        // a contract resolved from still-mutable options reports false regardless of the resolver.
        options.MakeReadOnly(populateMissingResolver: true);
        return GetCanUseSerializeHandler(options.GetTypeInfo(type));
    }

    // CanUseSerializeHandler is internal to STJ and is the only direct signal that the
    // generated fast path is in use; timing would be the alternative.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_CanUseSerializeHandler")]
    private static extern bool GetCanUseSerializeHandler(JsonTypeInfo typeInfo);

    public sealed class FastPathMigratableConverter : JsonConverter<FastPathMigratable>
    {
        public override FastPathMigratable Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Skip();
            return new FastPathMigratable("converter");
        }

        public override void Write(Utf8JsonWriter writer, FastPathMigratable value, JsonSerializerOptions options)
            => writer.WriteStringValue("converter");
    }
}

public record class FastPathPlain(string Name);

public record class FastPathUnregistered(string Name);

[JsonMigratable(TypeDiscriminator = "fast-path")]
public record class FastPathMigratable(string Name);

public record class FastPathWrapper(FastPathMigratable Inner);

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(FastPathPlain))]
[JsonSerializable(typeof(FastPathMigratable))]
[JsonSerializable(typeof(FastPathWrapper))]
public partial class FastPathJsonContext : JsonSerializerContext;
