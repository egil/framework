using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Tests;

/// <summary>
/// Migration support is an entry in <see cref="JsonSerializerOptions.TypeInfoResolverChain"/> and
/// must keep working when callers decorate that entry or edit copies of the options.
/// </summary>
public class ResolverChainTests
{
    private const string LegacyPayload = """{"$type":"chain-v1","Name":"Jane Doe"}""";

    [Fact]
    public void Decorated_chain_entry_keeps_migration()
    {
        // WithAddedModifier wraps the entry, so the migration resolver is no longer visible in the
        // chain by identity while STJ still delegates to it.
        var options = new JsonSerializerOptions();
        options.AddJsonMigrationSupport();
        options.TypeInfoResolverChain[0] = options.TypeInfoResolverChain[0].WithAddedModifier(static _ => { });

        var json = JsonSerializer.Serialize(new ChainV2("Jane", "Doe"), options);
        var migrated = JsonSerializer.Deserialize<ChainV2>(LegacyPayload, options);

        Assert.Equal("""{"$type":"chain-v2","FirstName":"Jane","LastName":"Doe"}""", json);
        Assert.Equal(new ChainV2("Jane", "Doe"), migrated);
    }

    [Fact]
    public void Decorated_chain_entry_followed_by_a_context_keeps_migration()
    {
        var options = new JsonSerializerOptions();
        options.AddJsonMigrationSupport();
        options.TypeInfoResolverChain[0] = options.TypeInfoResolverChain[0].WithAddedModifier(static _ => { });
        options.TypeInfoResolverChain.Add(ChainJsonContext.Default);

        var json = JsonSerializer.Serialize(new ChainWrapper(new ChainV2("Jane", "Doe")), options);
        var migrated = JsonSerializer.Deserialize<ChainV2>(LegacyPayload, options);

        Assert.Equal("""{"Inner":{"$type":"chain-v2","FirstName":"Jane","LastName":"Doe"}}""", json);
        Assert.Equal(new ChainV2("Jane", "Doe"), migrated);
    }

    [Fact]
    public void Converter_removed_from_a_copy_lets_migration_serve_the_type_there()
    {
        // A converter registered before migration support wins only while it is still in the
        // options being used; a copy without it must not leave the type without migration.
        var original = new JsonSerializerOptions();
        var converter = new ChainV2Converter();
        original.Converters.Add(converter);
        original.AddJsonMigrationSupport();

        var copy = new JsonSerializerOptions(original);
        copy.Converters.Remove(converter);

        Assert.Equal("\"converter\"", JsonSerializer.Serialize(new ChainV2("Jane", "Doe"), original));
        Assert.Equal("""{"$type":"chain-v2","FirstName":"Jane","LastName":"Doe"}""", JsonSerializer.Serialize(new ChainV2("Jane", "Doe"), copy));
        Assert.Equal(new ChainV2("Jane", "Doe"), JsonSerializer.Deserialize<ChainV2>(LegacyPayload, copy));
    }

    public sealed class ChainV2Converter : JsonConverter<ChainV2>
    {
        public override ChainV2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Skip();
            return new ChainV2("converter", "converter");
        }

        public override void Write(Utf8JsonWriter writer, ChainV2 value, JsonSerializerOptions options)
            => writer.WriteStringValue("converter");
    }
}

[JsonMigratable(TypeDiscriminator = "chain-v1")]
public record class ChainV1(string Name);

[JsonMigratable(TypeDiscriminator = "chain-v2")]
public record class ChainV2(string FirstName, string LastName) : IMigrateFrom<ChainV1, ChainV2>
{
    public static bool TryMigrateFrom(ChainV1 source, out ChainV2 result)
    {
        var names = source.Name.Split(' ');
        result = new ChainV2(names[0], names.Length > 1 ? names[1] : string.Empty);
        return true;
    }
}

public record class ChainWrapper(ChainV2 Inner);

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(ChainV1))]
[JsonSerializable(typeof(ChainV2))]
[JsonSerializable(typeof(ChainWrapper))]
public partial class ChainJsonContext : JsonSerializerContext;
