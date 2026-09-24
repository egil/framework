#if NET11_0_OR_GREATER
namespace Egil.SystemTextJson.Migration.Analyzers.Tests;

public sealed class UnionClassifierAnalyzerTests
{
    [Fact]
    public async Task Sdk_context_with_migratable_union_without_classifier_reports_precise_fix()
    {
        var result = await SdkAnalyzerFixture.BuildAsync(Source(""), typeof(UnionClassifierAnalyzer).Assembly.Location, TestContext.Current.CancellationToken);

        Assert.True(result.ExitCode == 0, result.Output);

        using var log = System.Text.Json.JsonDocument.Parse(result.Diagnostics);
        var diagnostics = log.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray().ToArray();
        var diagnostic = Assert.Single(diagnostics, diagnostic => diagnostic.GetProperty("ruleId").GetString() == "STJM0008");
        Assert.Contains("JsonUnion(TypeClassifier = typeof(JsonMigratableUnionTypeClassifier))", diagnostic.GetProperty("message").GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sdk_context_with_classifier_or_non_union_does_not_report()
    {
        var result = await SdkAnalyzerFixture.BuildAsync(Source("[JsonUnion(TypeClassifier = typeof(Egil.SystemTextJson.Migration.JsonMigratableUnionTypeClassifier))]"), typeof(UnionClassifierAnalyzer).Assembly.Location, TestContext.Current.CancellationToken);

        Assert.True(result.ExitCode == 0, result.Output);

        using var log = System.Text.Json.JsonDocument.Parse(result.Diagnostics);
        var diagnostics = log.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray();
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.GetProperty("ruleId").GetString() == "STJM0008");
    }

    [Fact]
    public async Task Sdk_context_with_inherited_migratable_union_case_without_classifier_reports()
    {
        var source = """
            using System.Text.Json; using System.Text.Json.Serialization; using System.Text.Json.Serialization.Metadata;
            namespace Egil.SystemTextJson.Migration { public sealed class JsonMigratableAttribute : System.Attribute { } public sealed class JsonMigratableUnionTypeClassifier : JsonTypeClassifierFactory { public override bool CanClassify(JsonTypeClassifierContext context) => true; public override JsonTypeClassifier CreateJsonClassifier(JsonTypeClassifierContext context, JsonSerializerOptions options) => throw new System.NotSupportedException(); } }
            [Egil.SystemTextJson.Migration.JsonMigratable] public class MarkedBase { }
            public sealed class DerivedCase : MarkedBase { }
            public union DerivedOrText(DerivedCase, string);
            [JsonSerializable(typeof(DerivedOrText))]
            public partial class MigrationContext : JsonSerializerContext { }
            """;
        var result = await SdkAnalyzerFixture.BuildAsync(source, typeof(UnionClassifierAnalyzer).Assembly.Location, TestContext.Current.CancellationToken);

        Assert.True(result.ExitCode == 0, result.Output);

        using var log = System.Text.Json.JsonDocument.Parse(result.Diagnostics);
        var diagnostics = log.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray();
        Assert.Contains(diagnostics, diagnostic => diagnostic.GetProperty("ruleId").GetString() == "STJM0008");
    }

    [Fact]
    public async Task Sdk_non_union_context_does_not_report()
    {
        var source = """
            using System.Text.Json; using System.Text.Json.Serialization; using System.Text.Json.Serialization.Metadata;
            namespace Egil.SystemTextJson.Migration { public sealed class JsonMigratableAttribute : System.Attribute { } public sealed class JsonMigratableUnionTypeClassifier : JsonTypeClassifierFactory { public override bool CanClassify(JsonTypeClassifierContext context) => true; public override JsonTypeClassifier CreateJsonClassifier(JsonTypeClassifierContext context, JsonSerializerOptions options) => throw new System.NotSupportedException(); } }
            [Egil.SystemTextJson.Migration.JsonMigratable] public sealed class Current { }
            [JsonSerializable(typeof(Current))]
            public partial class MigrationContext : JsonSerializerContext { }
            """;
        var result = await SdkAnalyzerFixture.BuildAsync(source, typeof(UnionClassifierAnalyzer).Assembly.Location, TestContext.Current.CancellationToken);

        Assert.True(result.ExitCode == 0, result.Output);

        using var log = System.Text.Json.JsonDocument.Parse(result.Diagnostics);
        var diagnostics = log.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray();
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.GetProperty("ruleId").GetString() == "STJM0008");
    }


    [Fact]
    public async Task Referenced_union_with_migratable_case_reports()
    {
        const string referencedSource = """
            using System.Text.Json; using System.Text.Json.Serialization; using System.Text.Json.Serialization.Metadata;
            namespace Egil.SystemTextJson.Migration
            {
                [System.AttributeUsage(System.AttributeTargets.Class, Inherited = true)]
                public sealed class JsonMigratableAttribute : System.Attribute { }
                public sealed class JsonMigratableUnionTypeClassifier : JsonTypeClassifierFactory { public override bool CanClassify(JsonTypeClassifierContext context) => true; public override JsonTypeClassifier CreateJsonClassifier(JsonTypeClassifierContext context, JsonSerializerOptions options) => throw new System.NotSupportedException(); }
            }
            namespace ReferenceModels
            {
                [Egil.SystemTextJson.Migration.JsonMigratable]
                public sealed class Current { }
                public union CurrentOrText(Current, string);
            }
            """;
        const string consumerSource = """
            using System.Text.Json; using System.Text.Json.Serialization; using System.Text.Json.Serialization.Metadata;
            using ReferenceModels;
            [JsonSerializable(typeof(CurrentOrText))]
            public partial class ConsumerContext : JsonSerializerContext { }
            """;
        var result = await SdkAnalyzerFixture.BuildAsync(
            consumerSource, typeof(UnionClassifierAnalyzer).Assembly.Location,
            TestContext.Current.CancellationToken, referencedSource);

        Assert.True(result.ExitCode == 0, result.Output);

        using var log = System.Text.Json.JsonDocument.Parse(result.Diagnostics);
        var diagnostics = log.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray();
        Assert.Single(diagnostics, diagnostic => diagnostic.GetProperty("ruleId").GetString() == "STJM0008");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.GetProperty("ruleId").GetString() == "AD0001");
    }

    [Fact]
    public async Task Nested_union_with_migratable_case_reports_on_outer_union()
    {
        const string source = """
            using System.Text.Json; using System.Text.Json.Serialization; using System.Text.Json.Serialization.Metadata;
            namespace Egil.SystemTextJson.Migration { public sealed class JsonMigratableAttribute : System.Attribute { } public sealed class JsonMigratableUnionTypeClassifier : JsonTypeClassifierFactory { public override bool CanClassify(JsonTypeClassifierContext context) => true; public override JsonTypeClassifier CreateJsonClassifier(JsonTypeClassifierContext context, JsonSerializerOptions options) => throw new System.NotSupportedException(); } }
            [Egil.SystemTextJson.Migration.JsonMigratable] public sealed class Current { }
            [JsonUnion(TypeClassifier = typeof(Egil.SystemTextJson.Migration.JsonMigratableUnionTypeClassifier))]
            public union Inner(Current, string);
            public union Outer(Inner, bool);
            [JsonSerializable(typeof(Outer))]
            public partial class MigrationContext : JsonSerializerContext { }
            """;
        var result = await SdkAnalyzerFixture.BuildAsync(source, typeof(UnionClassifierAnalyzer).Assembly.Location, TestContext.Current.CancellationToken);

        Assert.True(result.ExitCode == 0, result.Output);
        using var log = System.Text.Json.JsonDocument.Parse(result.Diagnostics);
        var diagnostics = log.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray();
        Assert.Single(diagnostics, diagnostic => diagnostic.GetProperty("ruleId").GetString() == "STJM0008");
    }

    [Fact]
    public async Task Nullable_migratable_struct_case_reports()
    {
        const string source = """
            using System.Text.Json; using System.Text.Json.Serialization; using System.Text.Json.Serialization.Metadata;
            namespace Egil.SystemTextJson.Migration { public sealed class JsonMigratableAttribute : System.Attribute { } public sealed class JsonMigratableUnionTypeClassifier : JsonTypeClassifierFactory { public override bool CanClassify(JsonTypeClassifierContext context) => true; public override JsonTypeClassifier CreateJsonClassifier(JsonTypeClassifierContext context, JsonSerializerOptions options) => throw new System.NotSupportedException(); } }
            [Egil.SystemTextJson.Migration.JsonMigratable] public struct Current { }
            public union CurrentOrText(Current?, string);
            [JsonSerializable(typeof(CurrentOrText))]
            public partial class MigrationContext : JsonSerializerContext { }
            """;
        var result = await SdkAnalyzerFixture.BuildAsync(source, typeof(UnionClassifierAnalyzer).Assembly.Location, TestContext.Current.CancellationToken);

        Assert.True(result.ExitCode == 0, result.Output);
        using var log = System.Text.Json.JsonDocument.Parse(result.Diagnostics);
        var diagnostics = log.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray();
        Assert.Single(diagnostics, diagnostic => diagnostic.GetProperty("ruleId").GetString() == "STJM0008");
    }
    private static string Source(string unionAttribute) => $$"""
        using System.Text.Json; using System.Text.Json.Serialization; using System.Text.Json.Serialization.Metadata;
        namespace Egil.SystemTextJson.Migration { public sealed class JsonMigratableAttribute : System.Attribute { } public sealed class JsonMigratableUnionTypeClassifier : JsonTypeClassifierFactory { public override bool CanClassify(JsonTypeClassifierContext context) => true; public override JsonTypeClassifier CreateJsonClassifier(JsonTypeClassifierContext context, JsonSerializerOptions options) => throw new System.NotSupportedException(); } }
        [Egil.SystemTextJson.Migration.JsonMigratable] public sealed class Current { }
        {{unionAttribute}}
        public union CurrentOrText(Current, string);
        [JsonSerializable(typeof(CurrentOrText))]
        [JsonSerializable(typeof(Current))]
        public partial class MigrationContext : JsonSerializerContext { }
        """;
}
#endif
