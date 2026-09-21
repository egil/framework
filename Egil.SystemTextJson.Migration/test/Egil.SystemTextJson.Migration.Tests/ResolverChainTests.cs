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
    public void Decorator_modifier_on_the_entry_applies_to_the_plain_contract_of_a_migratable_type()
    {
        // With no other resolver configured, the migration entry stands in for the default one,
        // so a modifier on that entry must see the plain contract of a migratable type too.
        var options = new JsonSerializerOptions();
        options.AddJsonMigrationSupport();
        options.TypeInfoResolverChain[0] = options.TypeInfoResolverChain[0].WithAddedModifier(static typeInfo =>
        {
            foreach (JsonPropertyInfo property in typeInfo.Properties)
            {
                if (property.Name == nameof(ChainV2.LastName))
                {
                    property.Name = "surname";
                }
            }
        });

        var json = JsonSerializer.Serialize(new ChainV2("Jane", "Doe"), options);
        var roundTripped = JsonSerializer.Deserialize<ChainV2>(json, options);

        Assert.Equal("""{"$type":"chain-v2","FirstName":"Jane","surname":"Doe"}""", json);
        Assert.Equal(new ChainV2("Jane", "Doe"), roundTripped);
    }

    [Fact]
    public void Adding_migration_support_twice_after_decorating_the_entry_keeps_the_first_registration()
    {
        // The decorated entry hides the resolver from the chain, so the idempotency guard must
        // discover the registration through the chain rather than by identity; otherwise the second
        // call would insert a fresh registry without the external migrator.
        var options = new JsonSerializerOptions();
        options.AddJsonMigrationSupport(static builder => builder.RegisterMigrator<ChainExternalMigrator>());
        options.TypeInfoResolverChain[0] = options.TypeInfoResolverChain[0].WithAddedModifier(static _ => { });
        options.AddJsonMigrationSupport();

        var migrated = JsonSerializer.Deserialize<ChainV3>(LegacyPayload, options);

        Assert.Equal(new ChainV3("Jane Doe"), migrated);
        Assert.Single(options.TypeInfoResolverChain);
    }

    [Fact]
    public void Replacing_the_resolver_then_adding_migration_support_again_registers_it()
    {
        // The first registration is gone once the resolver is replaced, so the second call must
        // not be treated as a duplicate.
        var options = new JsonSerializerOptions();
        options.AddJsonMigrationSupport();
        options.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        options.AddJsonMigrationSupport();

        var json = JsonSerializer.Serialize(new ChainV2("Jane", "Doe"), options);

        Assert.Equal("""{"$type":"chain-v2","FirstName":"Jane","LastName":"Doe"}""", json);
        Assert.Equal(2, options.TypeInfoResolverChain.Count);
    }

    [Fact]
    public void Clearing_the_chain_then_adding_migration_support_again_registers_it()
    {
        var options = new JsonSerializerOptions();
        options.AddJsonMigrationSupport();
        options.TypeInfoResolverChain.Clear();
        options.AddJsonMigrationSupport();

        var migrated = JsonSerializer.Deserialize<ChainV2>(LegacyPayload, options);

        Assert.Equal(new ChainV2("Jane", "Doe"), migrated);
    }

    [Fact]
    public void Registration_leaves_a_configured_resolver_mutable()
    {
        // DefaultJsonTypeInfoResolver freezes its Modifiers on its first GetTypeInfo call, so
        // registration must not resolve anything through the user's resolver.
        var resolver = new DefaultJsonTypeInfoResolver();
        var options = new JsonSerializerOptions { TypeInfoResolver = resolver };
        options.AddJsonMigrationSupport();
        resolver.Modifiers.Add(static typeInfo =>
        {
            foreach (JsonPropertyInfo property in typeInfo.Properties)
            {
                if (property.Name == nameof(ChainWrapper.Inner))
                {
                    property.Name = "inner";
                }
            }
        });

        var json = JsonSerializer.Serialize(new ChainWrapper(new ChainV2("Jane", "Doe")), options);

        Assert.Equal("""{"inner":{"$type":"chain-v2","FirstName":"Jane","LastName":"Doe"}}""", json);
    }

    [Fact]
    public void Whole_chain_decorated_keeps_the_downstream_resolver()
    {
        // A decorator around the whole chain leaves one visible entry; the migration resolver must
        // still let the resolver inside it serve the other types instead of standing in with
        // plain reflection.
        var options = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        ((DefaultJsonTypeInfoResolver)options.TypeInfoResolver).Modifiers.Add(static typeInfo =>
        {
            foreach (JsonPropertyInfo property in typeInfo.Properties)
            {
                if (property.Name == nameof(ChainWrapper.Inner))
                {
                    property.Name = "inner";
                }
            }
        });
        options.AddJsonMigrationSupport();
        options.TypeInfoResolver = JsonTypeInfoResolver.Combine(options.TypeInfoResolver!).WithAddedModifier(static _ => { });

        var json = JsonSerializer.Serialize(new ChainWrapper(new ChainV2("Jane", "Doe")), options);
        var migrated = JsonSerializer.Deserialize<ChainV2>(LegacyPayload, options);

        Assert.Single(options.TypeInfoResolverChain);
        Assert.Equal("""{"inner":{"$type":"chain-v2","FirstName":"Jane","LastName":"Doe"}}""", json);
        Assert.Equal(new ChainV2("Jane", "Doe"), migrated);
    }

    [Fact]
    public void Application_defined_wrapper_around_the_entry_keeps_migration_with_a_downstream_resolver()
    {
        // The wrapper is opaque to structural discovery, so the resolver must recognise its own
        // exclusion scope by identity while building a converter through the wrapper; otherwise
        // the type's converter would build itself again without end.
        var options = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        options.AddJsonMigrationSupport();
        options.TypeInfoResolverChain[0] = new ForwardingResolver(options.TypeInfoResolverChain[0]);

        var json = JsonSerializer.Serialize(new ChainWrapper(new ChainV2("Jane", "Doe")), options);
        var migrated = JsonSerializer.Deserialize<ChainV2>(LegacyPayload, options);

        Assert.Equal("""{"Inner":{"$type":"chain-v2","FirstName":"Jane","LastName":"Doe"}}""", json);
        Assert.Equal(new ChainV2("Jane", "Doe"), migrated);
    }

    [Fact]
    public void Re_registering_behind_an_application_defined_wrapper_keeps_migration_working()
    {
        // The wrapper hides the first registration from the guard, so a second resolver is added
        // ahead of it. Both are active; each must honour the other's exclusion scopes or the
        // type's converter would build itself again without end.
        var options = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        options.AddJsonMigrationSupport();
        options.TypeInfoResolverChain[0] = new ForwardingResolver(options.TypeInfoResolverChain[0]);
        options.AddJsonMigrationSupport();

        var json = JsonSerializer.Serialize(new ChainWrapper(new ChainV2("Jane", "Doe")), options);
        var migrated = JsonSerializer.Deserialize<ChainV2>(LegacyPayload, options);

        Assert.Equal(3, options.TypeInfoResolverChain.Count);
        Assert.Equal("""{"Inner":{"$type":"chain-v2","FirstName":"Jane","LastName":"Doe"}}""", json);
        Assert.Equal(new ChainV2("Jane", "Doe"), migrated);
    }

    [Fact]
    public void Application_defined_wrapper_without_a_downstream_resolver_reports_missing_metadata()
    {
        // An opaque wrapper counts as another resolver, so the reflection stand-in does not apply;
        // the failure is the same one STJ reports for any chain without a resolver for the type.
        var options = new JsonSerializerOptions();
        options.AddJsonMigrationSupport();
        options.TypeInfoResolverChain[0] = new ForwardingResolver(options.TypeInfoResolverChain[0]);

        var exception = Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(new ChainV2("Jane", "Doe"), options));

        Assert.Contains("No JSON metadata is available", exception.Message, StringComparison.Ordinal);
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

    public sealed class ForwardingResolver(IJsonTypeInfoResolver inner) : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
            => inner.GetTypeInfo(type, options);
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

[JsonMigratable(TypeDiscriminator = "chain-v3")]
public record class ChainV3(string FullName);

public sealed class ChainExternalMigrator : IMigrate<ChainV1, ChainV3>
{
    public bool TryMigrateFrom(ChainV1 source, out ChainV3 result)
    {
        result = new ChainV3(source.Name);
        return true;
    }
}

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(ChainV1))]
[JsonSerializable(typeof(ChainV2))]
[JsonSerializable(typeof(ChainWrapper))]
public partial class ChainJsonContext : JsonSerializerContext;
