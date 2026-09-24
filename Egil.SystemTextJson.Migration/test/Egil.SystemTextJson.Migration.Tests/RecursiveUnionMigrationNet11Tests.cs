#if NET11_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;
using static Egil.SystemTextJson.Migration.Tests.UnionMigrationTests;

namespace Egil.SystemTextJson.Migration.Tests;

public class RecursiveUnionMigrationTests
{
    [Fact]
    public void External_recursive_migration_preserves_children_already_in_the_current_format()
    {
        var options = CreateOptions();
        var json = """{"oldName":"root","children":[{"$type":"branch","label":"child","children":[]}]}""";

        var node = JsonSerializer.Deserialize<Node>(json, options);

        var root = Assert.IsType<Branch>(node.Value);
        var child = Assert.IsType<Branch>(Assert.Single(root.Children).Value);
        Assert.Equal("root", root.Label);
        Assert.Equal("child", child.Label);
        Assert.Empty(child.Children);
    }

    [Fact]
    public void External_recursive_migration_rejects_nested_old_object_sources()
    {
        var options = CreateOptions();
        var json = $$"""{"oldName":"root","children":[{"$type":"{{typeof(LegacyBranch).FullName}}","oldName":"child","children":[]}]}""";

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Node>(json, options));

        Assert.Contains("nested inside the migration of that same type", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(LegacyBranch).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void External_recursive_migration_rejects_nested_sources_with_an_object_converter()
    {
        var options = CreateOptions(builder => builder.RegisterMigrator<LegacyBox, Branch, BoxBranchMigrator>());
        var json = $$"""{"$type":"{{typeof(LegacyBranch).FullName}}","oldName":"root","children":[{"$type":"{{typeof(LegacyBox).FullName}}","payload":"child"}]}""";

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Node>(json, options));

        Assert.Contains("nested inside the migration of that same type", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(LegacyBox).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("42", "42")]
    [InlineData("\"child\"", "child")]
    [InlineData("{\"oldName\":\"child\",\"children\":[]}", "child")]
    public void External_recursive_migration_rejects_nested_sources_without_discriminators(string childJson, string expectedLabel)
    {
        var options = CreateOptions(builder => builder.RegisterMigrator<ScalarBranchMigrator>());
        var json = $$"""{"$type":"{{typeof(LegacyBranch).FullName}}","oldName":"root","children":[{{childJson}}]}""";

        var direct = JsonSerializer.Deserialize<Node>(childJson, options);
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Node>(json, options));

        Assert.Equal(expectedLabel, Assert.IsType<Branch>(direct.Value).Label);
        Assert.Contains("without a leading type discriminator", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(Branch).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void External_recursive_migration_rejects_nested_sources_with_a_scalar_converter()
    {
        var options = CreateOptions(builder => builder.RegisterMigrator<PlainColour, Branch, ColourBranchMigrator>());
        options.Converters.Add(new JsonStringEnumConverter());
        var json = $$"""{"$type":"{{typeof(LegacyBranch).FullName}}","oldName":"root","children":["Red"]}""";

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Node>(json, options));

        Assert.Contains("cannot classify a JSON string", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(Branch).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Recursive_nested_unions_keep_the_configured_discriminator()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            .AddJsonMigrationSupport(builder => builder.SetTypeDiscriminatorPropertyName("kind"));
        var json = """{"kind":"nested-branch","label":"root","children":[{"kind":"nested-branch","label":"child","children":[]}]}""";

        var value = JsonSerializer.Deserialize<NestedNode>(json, options);

        var root = Assert.IsType<NestedBranch>(Assert.IsType<NestedBranchOrLeaf>(value.Value).Value);
        var childNode = Assert.Single(root.Children);
        var child = Assert.IsType<NestedBranch>(Assert.IsType<NestedBranchOrLeaf>(childNode.Value).Value);
        Assert.Equal("child", child.Label);
        Assert.Empty(child.Children);
    }

    private static JsonSerializerOptions CreateOptions(Action<JsonMigrationBuilder>? configure = null)
        => new JsonSerializerOptions(JsonSerializerDefaults.Web)
            .AddJsonMigrationSupport(builder =>
            {
                builder.RegisterMigrator<LegacyBranch, Branch, BranchMigrator>();
                configure?.Invoke(builder);
            });

    // A historical wire type can reference the current union, so only children already
    // in the current format can be read while its own migration converter is being built.
    public record LegacyBranch(string OldName, Node[] Children);

    [JsonMigratable(TypeDiscriminator = "branch", UndiscriminatedSourceType = typeof(LegacyBranch))]
    public record Branch(string Label, Node[] Children);

    public union Node(Branch, Leaf);

    [JsonMigratable(TypeDiscriminator = "nested-branch")]
    public record NestedBranch(string Label, NestedNode[] Children);

    public union NestedBranchOrLeaf(NestedBranch, Leaf);

    public union NestedNode(NestedBranchOrLeaf, bool);

    public sealed class BranchMigrator : IMigrate<LegacyBranch, Branch>
    {
        public bool TryMigrateFrom(LegacyBranch source, out Branch result)
        {
            result = new Branch(source.OldName, source.Children);
            return true;
        }
    }

    public sealed class BoxBranchMigrator : IMigrate<LegacyBox, Branch>
    {
        public bool TryMigrateFrom(LegacyBox source, out Branch result)
        {
            result = new Branch(source.Payload, []);
            return true;
        }
    }

    public sealed class ScalarBranchMigrator : IMigrate<int, Branch>, IMigrate<string, Branch>
    {
        public bool TryMigrateFrom(int source, out Branch result)
        {
            result = new Branch(source.ToString(System.Globalization.CultureInfo.InvariantCulture), []);
            return true;
        }

        public bool TryMigrateFrom(string source, out Branch result)
        {
            result = new Branch(source, []);
            return true;
        }
    }

    public sealed class ColourBranchMigrator : IMigrate<PlainColour, Branch>
    {
        public bool TryMigrateFrom(PlainColour source, out Branch result)
        {
            result = new Branch(source.ToString(), []);
            return true;
        }
    }
}
#endif
