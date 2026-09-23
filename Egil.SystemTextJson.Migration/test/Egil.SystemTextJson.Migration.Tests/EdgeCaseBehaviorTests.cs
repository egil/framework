using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.SystemTextJson.Migration.Tests;

public class EdgeCaseBehaviorTests
{
    [Fact]
    public void Deserialize_list_of_migratable_types_without_migration()
    {
        var options = CreateOptions();
        var list = new List<ListItemV2>
        {
            new("Jane", "Doe"),
            new("Jane", "Doe"),
        };

        var json = JsonSerializer.Serialize(list, options);
        var result = JsonSerializer.Deserialize<List<ListItemV2>>(json, options);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("Jane", result[0].FirstName);
        Assert.Equal("Doe", result[1].LastName);
    }

    [Fact]
    public void Deserialize_list_of_migratable_types_with_migration()
    {
        var options = CreateOptions();
        var json = JsonSerializer.Serialize(
            new List<ListItemV1> { new("Jane Doe"), new("Jane Doe") },
            options);

        var result = JsonSerializer.Deserialize<List<ListItemV2>>(json, options);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("Jane", result[0].FirstName);
        Assert.Equal("Doe", result[0].LastName);
        Assert.Equal("Jane", result[1].FirstName);
        Assert.Equal("Doe", result[1].LastName);
    }

    [Fact]
    public void Deserialize_array_of_migratable_types_without_migration()
    {
        var options = CreateOptions();
        var array = new[] { new ListItemV2("Jane", "Doe") };

        var json = JsonSerializer.Serialize(array, options);
        var result = JsonSerializer.Deserialize<ListItemV2[]>(json, options);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Jane", result[0].FirstName);
    }

    [Fact]
    public void Deserialize_array_of_migratable_types_with_migration()
    {
        var options = CreateOptions();
        var json = JsonSerializer.Serialize(
            new[] { new ListItemV1("Jane Doe") },
            options);

        var result = JsonSerializer.Deserialize<ListItemV2[]>(json, options);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Jane", result[0].FirstName);
        Assert.Equal("Doe", result[0].LastName);
    }

    [Fact]
    public void Deserialize_dictionary_with_migratable_values_without_migration()
    {
        var options = CreateOptions();
        var dict = new Dictionary<string, ListItemV2>
        {
            ["user1"] = new("Jane", "Doe"),
        };

        var json = JsonSerializer.Serialize(dict, options);
        var result = JsonSerializer.Deserialize<Dictionary<string, ListItemV2>>(json, options);

        Assert.NotNull(result);
        Assert.True(result.ContainsKey("user1"));
        Assert.Equal("Jane", result["user1"].FirstName);
    }

    [Fact]
    public void Deserialize_dictionary_with_migratable_values_with_migration()
    {
        var options = CreateOptions();
        var json = JsonSerializer.Serialize(
            new Dictionary<string, ListItemV1> { ["user1"] = new("Jane Doe") },
            options);

        var result = JsonSerializer.Deserialize<Dictionary<string, ListItemV2>>(json, options);

        Assert.NotNull(result);
        Assert.True(result.ContainsKey("user1"));
        Assert.Equal("Jane", result["user1"].FirstName);
        Assert.Equal("Doe", result["user1"].LastName);
    }

    [Fact]
    public void Deserialize_null_json_value_for_nullable_migratable_type()
    {
        var options = CreateOptions();

        var result = JsonSerializer.Deserialize<ListItemV2?>("null", options);

        Assert.Null(result);
    }

    [Fact]
    public async Task Deserialize_async_stream_without_migration()
    {
        var options = CreateOptions();
        var data = new ListItemV2("Jane", "Doe");
        var json = JsonSerializer.SerializeToUtf8Bytes(data, options);

        using var stream = new MemoryStream(json);
        var result = await JsonSerializer.DeserializeAsync<ListItemV2>(stream, options, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Jane", result.FirstName);
        Assert.Equal("Doe", result.LastName);
    }

    [Fact]
    public async Task Deserialize_async_stream_with_migration()
    {
        var options = CreateOptions();
        var json = JsonSerializer.SerializeToUtf8Bytes(new ListItemV1("Jane Doe"), options);

        using var stream = new MemoryStream(json);
        var result = await JsonSerializer.DeserializeAsync<ListItemV2>(stream, options, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Jane", result.FirstName);
        Assert.Equal("Doe", result.LastName);
    }

    [Fact]
    public void Deserialize_with_unmapped_member_handling_disallow()
    {
        var options = CreateOptions();
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;

        var data = new ListItemV2("Jane", "Doe");
        var json = JsonSerializer.Serialize(data, options);
        var result = JsonSerializer.Deserialize<ListItemV2>(json, options);

        Assert.NotNull(result);
        Assert.Equal("Jane", result.FirstName);
    }

    [Fact]
    public void Deserialize_with_unmapped_member_handling_disallow_and_migration()
    {
        var options = CreateOptions();
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;

        var json = JsonSerializer.Serialize(new ListItemV1("Jane Doe"), options);
        var result = JsonSerializer.Deserialize<ListItemV2>(json, options);

        Assert.NotNull(result);
        Assert.Equal("Jane", result.FirstName);
        Assert.Equal("Doe", result.LastName);
    }

    [Fact]
    public void Deserialize_with_discriminator_not_first_property_treated_as_legacy()
    {
        var options = CreateOptions();

        // JSON with $type NOT as the first property — treated as legacy payload.
        var json = """{"firstName":"Jane","lastName":"Doe","$type":"not-first"}""";
        var result = JsonSerializer.Deserialize<ListItemV2>(json, options);

        Assert.NotNull(result);
        Assert.Equal("Jane", result.FirstName);
        Assert.Equal("Doe", result.LastName);
    }

    [Fact]
    public void Deserialize_legacy_payload_without_discriminator()
    {
        var options = CreateOptions();

        var json = """{"firstName":"Jane","lastName":"Doe"}""";
        var result = JsonSerializer.Deserialize<ListItemV2>(json, options);

        Assert.NotNull(result);
        Assert.Equal("Jane", result.FirstName);
        Assert.Equal("Doe", result.LastName);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("\"unknown\"")]
    public void Invalid_source_specific_discriminator_does_not_invoke_migration(string discriminator)
    {
        var options = new JsonSerializerOptions().AddJsonMigrationSupport();

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RenamedTarget>(
            $"{{\"version\":{discriminator},\"Value\":42}}", options));
    }

    [Theory]
    [InlineData("a", 1)]
    [InlineData("bb", 2)]
    [InlineData("cc", 3)]
    [InlineData("dc", 6)]
    [InlineData("ddd", 4)]
    [InlineData("éé", 5)]
    [InlineData("\\u0063c", 3)]
    [InlineData("\\u00e9\\u00e9", 5)]
    public void Many_sources_match_decoded_discriminators_in_contiguous_and_segmented_json(string discriminator, int expectedVersion)
    {
        var options = ManySourceOptions();
        byte[] payload = Encoding.UTF8.GetBytes($"{{\"$type\":\"{discriminator}\",\"value\":42}}");
        var first = new Segment(payload.AsMemory(0, 12));
        var last = first.Append(payload.AsMemory(12));
        var reader = new Utf8JsonReader(new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length));

        var contiguous = JsonSerializer.Deserialize<VersionTarget>(payload, options);
        var segmented = JsonSerializer.Deserialize<VersionTarget>(ref reader, options);

        Assert.Equal(new VersionTarget(42, expectedVersion), contiguous);
        Assert.Equal(contiguous, segmented);
        Assert.Equal(JsonTokenType.EndObject, reader.TokenType);
    }

    [Theory]
    [InlineData("zz")]
    [InlineData("zc")]
    [InlineData("unknown-length")]
    [InlineData("\\u007az")]
    public void Many_sources_reject_unknown_discriminators(string discriminator)
    {
        var options = ManySourceOptions();

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<VersionTarget>(
            $"{{\"$type\":\"{discriminator}\",\"value\":42}}", options));

        Assert.Contains("No migrator was found", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("prefix-c", 3, false)]
    [InlineData("prefix-f", 6, false)]
    [InlineData("c-suffix", 3, true)]
    [InlineData("f-suffix", 6, true)]
    public void Same_length_discriminators_match_the_entire_value(string discriminator, int expectedVersion, bool commonSuffix)
    {
        var options = UniformSourceOptions(commonSuffix);

        var result = JsonSerializer.Deserialize<VersionTarget>($"{{\"$type\":\"{discriminator}\",\"value\":42}}", options);

        Assert.Equal(new VersionTarget(42, expectedVersion), result);
    }

    [Theory]
    [InlineData("prefix-z", false)]
    [InlineData("z-suffix", true)]
    [InlineData("", false)]
    public void Same_length_discriminators_reject_unknown_values(string discriminator, bool commonSuffix)
    {
        var options = UniformSourceOptions(commonSuffix);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<VersionTarget>($"{{\"$type\":\"{discriminator}\",\"value\":42}}", options));
    }

    [Theory]
    [InlineData("", 3)]
    [InlineData("version-target", 7)]
    public void Many_sources_preserve_empty_registered_discriminators(string discriminator, int expectedVersion)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            .AddJsonMigrationSupport(builder => RegisterVersionSources(builder)
                .GetTypeDiscriminatorFrom<JsonMigratableAttribute>(attribute => attribute.TypeDiscriminator == "cc" ? "" : attribute.TypeDiscriminator ?? "version-target"));

        var result = JsonSerializer.Deserialize<VersionTarget>($"{{\"$type\":\"{discriminator}\",\"value\":42,\"version\":7}}", options);

        Assert.Equal(new VersionTarget(42, expectedVersion), result);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();
        return options;
    }

    private static JsonSerializerOptions ManySourceOptions() => new JsonSerializerOptions(JsonSerializerDefaults.Web)
        .AddJsonMigrationSupport(builder => RegisterVersionSources(builder));

    private static JsonSerializerOptions UniformSourceOptions(bool commonSuffix) => new JsonSerializerOptions(JsonSerializerDefaults.Web)
        .AddJsonMigrationSupport(builder => RegisterVersionSources(builder)
            .GetTypeDiscriminatorFrom<JsonMigratableAttribute>(attribute =>
            {
                string code = attribute.TypeDiscriminator switch
                {
                    "a" => "a", "bb" => "b", "cc" => "c", "ddd" => "d", "éé" => "e", "dc" => "f", _ => "target",
                };
                return commonSuffix ? code + "-suffix" : "prefix-" + code;
            }));

    private static JsonMigrationBuilder RegisterVersionSources(JsonMigrationBuilder builder) => builder.RegisterMigrator<VersionMigrator<SourceA>>()
        .RegisterMigrator<VersionMigrator<SourceB>>()
        .RegisterMigrator<VersionMigrator<SourceC>>()
        .RegisterMigrator<VersionMigrator<SourceD>>()
        .RegisterMigrator<VersionMigrator<SourceE>>()
        .RegisterMigrator<VersionMigrator<SourceF>>();

    [JsonMigratable(TypeDiscriminator = "renamed-v1", TypeDiscriminatorPropertyName = "version")]
    public sealed record RenamedSource(int Value);

    [JsonMigratable]
    public sealed record RenamedTarget(int Value) : IMigrateFrom<RenamedSource, RenamedTarget>
    {
        public static bool TryMigrateFrom(RenamedSource source, out RenamedTarget result)
            => throw new InvalidOperationException("An invalid discriminator must not reach migration.");
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public Segment Append(ReadOnlyMemory<byte> next)
        {
            var segment = new Segment(next) { RunningIndex = RunningIndex + Memory.Length };
            Next = segment;
            return segment;
        }
    }

    public interface IVersionSource { int Value { get; } int Version { get; } }

    [JsonMigratable(TypeDiscriminator = "a")]
    public sealed record SourceA(int Value) : IVersionSource { public int Version => 1; }

    [JsonMigratable(TypeDiscriminator = "bb")]
    public sealed record SourceB(int Value) : IVersionSource { public int Version => 2; }

    [JsonMigratable(TypeDiscriminator = "cc")]
    public sealed record SourceC(int Value) : IVersionSource { public int Version => 3; }

    [JsonMigratable(TypeDiscriminator = "ddd")]
    public sealed record SourceD(int Value) : IVersionSource { public int Version => 4; }

    [JsonMigratable(TypeDiscriminator = "éé")]
    public sealed record SourceE(int Value) : IVersionSource { public int Version => 5; }

    [JsonMigratable(TypeDiscriminator = "dc")]
    public sealed record SourceF(int Value) : IVersionSource { public int Version => 6; }

    [JsonMigratable]
    public sealed record VersionTarget(int Value, int Version);

    public sealed class VersionMigrator<TSource> : IMigrate<TSource, VersionTarget>
        where TSource : IVersionSource
    {
        public bool TryMigrateFrom(TSource source, out VersionTarget result)
        {
            result = new(source.Value, source.Version);
            return true;
        }
    }

    [JsonMigratable(TypeDiscriminator = "list-item-v1")]
    public record class ListItemV1(string Name);

    [JsonMigratable(TypeDiscriminator = "list-item-v2")]
    public record class ListItemV2(string FirstName, string LastName) : IMigrateFrom<ListItemV1, ListItemV2>
    {
        public static bool TryMigrateFrom(ListItemV1 source, out ListItemV2 result)
        {
            var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            result = new ListItemV2(
                names.Length > 0 ? names[0] : string.Empty,
                names.Length > 1 ? names[1] : string.Empty);
            return true;
        }
    }
}
