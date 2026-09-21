#if NET11_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.SystemTextJson.Migration.Tests;

/// <summary>
/// .NET 11 adds the PascalCase naming policy, per-member naming policy overrides and type-level
/// ignore defaults. The injected discriminator and migration must survive each of them with both
/// the reflection and the source-generated resolver.
/// </summary>
public partial class JsonSerializerOptionsCombinationsTests
{
    public static TheoryData<string, bool> NamingPolicyMatrix => new()
    {
        { "pascal", false },
        { "pascal", true },
        { "camel", false },
        { "camel", true },
        { "none", false },
        { "none", true },
    };

    [Theory]
    [MemberData(nameof(NamingPolicyMatrix))]
    public void Serialize_applies_naming_policy_member_override_and_type_level_ignore(string policy, bool sourceGenerated)
    {
        var options = CreateNamingOptions(policy, sourceGenerated);
        var current = new NamingV2("Jane", "Doe", "Aarhus", 42, null, new NamingChildV2("Kit", null));

        var json = JsonSerializer.Serialize(current, options);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var firstProperty = root.EnumerateObject().First();
        Assert.Equal("$type", firstProperty.Name);
        Assert.Equal("naming-v2", firstProperty.Value.GetString());
        Assert.Equal("Jane", root.GetProperty(ApplyPolicy(policy, "FirstName")).GetString());
        Assert.Equal("Aarhus", root.GetProperty("home_town").GetString());
        Assert.Equal(42, root.GetProperty("explicit_age").GetInt32());
        Assert.False(root.TryGetProperty(ApplyPolicy(policy, "NickName"), out _));
        var child = root.GetProperty(ApplyPolicy(policy, "Child"));
        Assert.Equal("child-v2", child.GetProperty("$type").GetString());
        Assert.False(child.TryGetProperty(ApplyPolicy(policy, "Note"), out _));
    }

    [Theory]
    [MemberData(nameof(NamingPolicyMatrix))]
    public void Round_trip_preserves_values_for_each_naming_policy(string policy, bool sourceGenerated)
    {
        var options = CreateNamingOptions(policy, sourceGenerated);
        var current = new NamingV2("Jane", "Doe", "Aarhus", 42, "JD", new NamingChildV2("Kit", "note"));

        var json = JsonSerializer.Serialize(current, options);
        var roundTripped = JsonSerializer.Deserialize<NamingV2>(json, options);

        Assert.NotNull(roundTripped);
        Assert.Equal(current with { MigratedDuringDeserialization = false }, roundTripped with { MigratedDuringDeserialization = false });
        Assert.False(roundTripped.MigratedDuringDeserialization);
    }

    [Theory]
    [MemberData(nameof(NamingPolicyMatrix))]
    public void Migration_and_nested_migration_work_for_each_naming_policy(string policy, bool sourceGenerated)
    {
        var options = CreateNamingOptions(policy, sourceGenerated);
        var legacy = new NamingV1("Jane Doe", "Aarhus", 42, new NamingChildV1("Kit"));

        var legacyJson = JsonSerializer.Serialize(legacy, options);
        var migrated = JsonSerializer.Deserialize<NamingV2>(legacyJson, options);

        Assert.NotNull(migrated);
        Assert.True(migrated.MigratedDuringDeserialization);
        Assert.Equal("Jane", migrated.FirstName);
        Assert.Equal("Doe", migrated.LastName);
        Assert.Equal("Aarhus", migrated.HomeTown);
        Assert.Equal(42, migrated.Age);
        Assert.NotNull(migrated.Child);
        Assert.True(migrated.Child.MigratedDuringDeserialization);
        Assert.Equal("Kit", migrated.Child.Name);
    }

    private static string ApplyPolicy(string policy, string name)
        => policy switch
        {
            "pascal" => JsonNamingPolicy.PascalCase.ConvertName(name),
            "camel" => JsonNamingPolicy.CamelCase.ConvertName(name),
            _ => name,
        };

    private static JsonSerializerOptions CreateNamingOptions(string policy, bool sourceGenerated)
    {
        var options = sourceGenerated
            ? new JsonSerializerOptions(NamingJsonContext.Default.Options)
            : new JsonSerializerOptions();

        options.PropertyNamingPolicy = policy switch
        {
            "pascal" => JsonNamingPolicy.PascalCase,
            "camel" => JsonNamingPolicy.CamelCase,
            _ => null,
        };

        options.AddJsonMigrationSupport();
        return options;
    }

    [JsonMigratable(TypeDiscriminator = "child-v1")]
    public record class NamingChildV1(string Name);

    [JsonMigratable(TypeDiscriminator = "child-v2")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public record class NamingChildV2(string Name, string? Note) : IJsonMigrationTracked, IMigrateFrom<NamingChildV1, NamingChildV2>
    {
        [JsonIgnore]
        public bool MigratedDuringDeserialization { get; set; }

        public static bool TryMigrateFrom(NamingChildV1 source, out NamingChildV2 result)
        {
            result = new NamingChildV2(source.Name, null);
            return true;
        }
    }

    [JsonMigratable(TypeDiscriminator = "naming-v1")]
    public record class NamingV1(
        string FullName,
        [property: JsonNamingPolicy(JsonKnownNamingPolicy.SnakeCaseLower)] string HomeTown,
        [property: JsonPropertyName("explicit_age")] int Age,
        NamingChildV1? Child);

    [JsonMigratable(TypeDiscriminator = "naming-v2")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public record class NamingV2(
        string FirstName,
        string LastName,
        [property: JsonNamingPolicy(JsonKnownNamingPolicy.SnakeCaseLower)] string HomeTown,
        [property: JsonPropertyName("explicit_age")] int Age,
        string? NickName,
        NamingChildV2? Child) : IJsonMigrationTracked, IMigrateFrom<NamingV1, NamingV2>
    {
        [JsonIgnore]
        public bool MigratedDuringDeserialization { get; set; }

        public static bool TryMigrateFrom(NamingV1 source, out NamingV2 result)
        {
            var names = source.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            result = new NamingV2(
                names.Length > 0 ? names[0] : string.Empty,
                names.Length > 1 ? names[1] : string.Empty,
                source.HomeTown,
                source.Age,
                null,
                source.Child is null ? null : new NamingChildV2(source.Child.Name, null) { MigratedDuringDeserialization = true });
            return true;
        }
    }

    [JsonSerializable(typeof(NamingV1))]
    [JsonSerializable(typeof(NamingV2))]
    [JsonSerializable(typeof(NamingChildV1))]
    [JsonSerializable(typeof(NamingChildV2))]
    public partial class NamingJsonContext : JsonSerializerContext;
}
#endif
