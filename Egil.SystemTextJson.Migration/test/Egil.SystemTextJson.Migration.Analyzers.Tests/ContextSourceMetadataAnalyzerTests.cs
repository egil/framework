using Microsoft.CodeAnalysis;

namespace Egil.SystemTextJson.Migration.Analyzers.Tests;

public sealed class ContextSourceMetadataAnalyzerTests
{
    private const string Contracts = """
        namespace Egil.SystemTextJson.Migration
        {
            public interface IMigrate<TSource, TTarget> { }
            public interface IMigrateFrom<TSource, TTarget> { }
            public sealed class JsonMigratableAttribute : System.Attribute { }
        }
        namespace System.Text.Json.Serialization
        {
            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class JsonSerializableAttribute : System.Attribute { public JsonSerializableAttribute(System.Type type) { } }
            public abstract class JsonSerializerContext { }
        }
        """;

    private const string SdkContracts = """
        namespace Egil.SystemTextJson.Migration
        {
            public interface IMigrate<TSource, TTarget> { }
            public interface IMigrateFrom<TSource, TTarget> { }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class JsonMigratableAttribute : System.Attribute { }
        }
        """;

    [Fact]
    public async Task Context_with_migratable_target_and_missing_source_metadata_reports_STJM0006()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Previous, Current> { }
            public class Previous { }
            [System.Text.Json.Serialization.JsonSerializable(typeof(Current))]
            public sealed class AppContext : System.Text.Json.Serialization.JsonSerializerContext { }
            """);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new ContextSourceMetadataAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("STJM0006", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        var sourceText = await diagnostic.Location.SourceTree!.GetTextAsync(TestContext.Current.CancellationToken);
        Assert.Equal("System.Text.Json.Serialization.JsonSerializable(typeof(Current))", sourceText.ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public async Task Context_that_lists_target_and_required_source_does_not_report_STJM0006()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Previous, Current> { }
            public class Previous { }
            [System.Text.Json.Serialization.JsonSerializable(typeof(Previous))]
            [System.Text.Json.Serialization.JsonSerializable(typeof(Current))]
            public sealed class AppContext : System.Text.Json.Serialization.JsonSerializerContext { }
            """);

        Assert.Empty(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new ContextSourceMetadataAnalyzer()));
    }

    [Fact]
    public async Task Context_that_lists_an_array_migration_source_does_not_report_STJM0006()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Previous[], Current> { }
            public class Previous { }
            [System.Text.Json.Serialization.JsonSerializable(typeof(Previous[]))]
            [System.Text.Json.Serialization.JsonSerializable(typeof(Current))]
            public sealed class AppContext : System.Text.Json.Serialization.JsonSerializerContext { }
            """);

        Assert.Empty(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new ContextSourceMetadataAnalyzer()));
    }

    [Fact]
    public async Task Context_that_omits_an_array_migration_source_reports_STJM0006()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Previous[], Current> { }
            public class Previous { }
            [System.Text.Json.Serialization.JsonSerializable(typeof(Current))]
            public sealed class AppContext : System.Text.Json.Serialization.JsonSerializerContext { }
            """);

        var diagnostic = Assert.Single(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new ContextSourceMetadataAnalyzer()));

        Assert.Equal("STJM0006", diagnostic.Id);
        Assert.Contains("Previous[]", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unrelated_context_does_not_report_STJM0006()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Previous, Current> { }
            public class Previous { }
            public class Plain { }
            [System.Text.Json.Serialization.JsonSerializable(typeof(Plain))]
            public sealed class AppContext : System.Text.Json.Serialization.JsonSerializerContext { }
            """);

        Assert.Empty(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new ContextSourceMetadataAnalyzer()));
    }

#if NET11_0_OR_GREATER
    [Theory]
    [InlineData("Current", "", false, 0)]
    [InlineData("Wrapper", "[System.Text.Json.Serialization.JsonSerializable(typeof(Ordinary))]", true, 1)]
    public async Task Sdk_custom_converter_contracts_do_not_invent_migration_requirements(
        string convertedType, string extraRegistration, bool ordinaryWarning, int warningCount)
    {
        var result = await SdkAnalyzerFixture.BuildAsync(SdkContracts + $$"""
            [Egil.SystemTextJson.Migration.JsonMigratable]
            [System.Text.Json.Serialization.JsonConverter(typeof(CustomConverter))]
            public class {{convertedType}} : Egil.SystemTextJson.Migration.IMigrateFrom<Previous, {{convertedType}}>
            {
                public HiddenTarget Value { get; set; }
            }
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class HiddenTarget : Egil.SystemTextJson.Migration.IMigrateFrom<Previous, HiddenTarget> { }
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Ordinary : Egil.SystemTextJson.Migration.IMigrateFrom<Previous, Ordinary> { }
            public class Previous { }
            public sealed class CustomConverter : System.Text.Json.Serialization.JsonConverter<{{convertedType}}>
            {
                public override {{convertedType}} Read(ref System.Text.Json.Utf8JsonReader reader, System.Type type, System.Text.Json.JsonSerializerOptions options) => new();
                public override void Write(System.Text.Json.Utf8JsonWriter writer, {{convertedType}} value, System.Text.Json.JsonSerializerOptions options) => writer.WriteStringValue("custom");
            }
            [System.Text.Json.Serialization.JsonSerializable(typeof({{convertedType}}))]
            {{extraRegistration}}
            public partial class AppContext : System.Text.Json.Serialization.JsonSerializerContext { }
            public static class Program
            {
                public static void Main()
                {
                    if (AppContext.Default.GetTypeInfo(typeof(HiddenTarget)) is not null || AppContext.Default.GetTypeInfo(typeof(Previous)) is not null)
                        throw new System.InvalidOperationException("Converter-owned members must not supply generated metadata.");
                    if (System.Text.Json.JsonSerializer.Serialize(new {{convertedType}}(), AppContext.Default.{{convertedType}}) != "\"custom\"")
                        throw new System.InvalidOperationException("Expected the converter to own the contract.");
                }
            }
            """, typeof(ContextSourceMetadataAnalyzer).Assembly.Location, TestContext.Current.CancellationToken, execute: true);

        Assert.True(result.ExitCode == 0, result.Output);
        var diagnostics = GetDiagnostics(result.Diagnostics);
        Assert.Equal(warningCount, diagnostics.Length);
        Assert.Equal(ordinaryWarning, diagnostics.Any(diagnostic => diagnostic.GetProperty("message").GetProperty("text").GetString()!.Contains("'Ordinary'", System.StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Sdk_generated_context_with_transitive_public_source_property_does_not_report_STJM0006()
    {
        var result = await SdkAnalyzerFixture.BuildAsync(SdkContracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Previous, Current>
            {
                public Previous Snapshot { get; set; } = new();
            }
            public class Previous { }
            [System.Text.Json.Serialization.JsonSerializable(typeof(Current))]
            public partial class AppContext : System.Text.Json.Serialization.JsonSerializerContext { }
            public static class MetadataProof
            {
                public static System.Text.Json.Serialization.Metadata.JsonTypeInfo<Previous> Previous => AppContext.Default.Previous;
            }
            """, typeof(ContextSourceMetadataAnalyzer).Assembly.Location, TestContext.Current.CancellationToken);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Empty(GetDiagnostics(result.Diagnostics));
    }

    [Fact]
    public async Task Sdk_generated_context_with_array_root_generates_element_source_metadata_without_STJM0006()
    {
        var result = await SdkAnalyzerFixture.BuildAsync(SdkContracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Previous, Current> { }
            public class Previous { }
            [System.Text.Json.Serialization.JsonSerializable(typeof(Current))]
            [System.Text.Json.Serialization.JsonSerializable(typeof(Previous[]))]
            public partial class AppContext : System.Text.Json.Serialization.JsonSerializerContext { }
            public static class MetadataProof
            {
                public static System.Text.Json.Serialization.Metadata.JsonTypeInfo<Previous> Previous => AppContext.Default.Previous;
            }
            """, typeof(ContextSourceMetadataAnalyzer).Assembly.Location, TestContext.Current.CancellationToken);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Empty(GetDiagnostics(result.Diagnostics));
    }

    [Theory]
    [InlineData("public class Holder<T> { public int Value { get; set; } }", "", "[System.Text.Json.Serialization.JsonSerializable(typeof(Holder<Previous>))]", false, 1)]
    [InlineData("", "[System.Text.Json.Serialization.JsonIgnore] public Previous Snapshot { get; set; }", "", false, 1)]
    [InlineData("", "[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Always)] public Previous Snapshot { get; set; }", "", false, 1)]
    [InlineData("", "[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)] public Previous Snapshot { get; set; }", "", true, 0)]
    [InlineData("", "[System.Text.Json.Serialization.JsonInclude] internal Previous Snapshot { get; set; }", "", true, 0)]
    [InlineData("", "", "[System.Text.Json.Serialization.JsonSerializable(typeof(System.Collections.Generic.List<Previous>))]", true, 0)]
    public async Task Sdk_metadata_reachability_matches_ignored_members_generic_arguments_and_collection_elements(
        string extraTypes, string member, string extraRegistration, bool hasSourceMetadata, int warningCount)
    {
        var source = SdkContracts + $$"""
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Previous, Current> { {{member}} }
            public class Previous { }
            {{extraTypes}}
            [System.Text.Json.Serialization.JsonSerializable(typeof(Current))]
            {{extraRegistration}}
            public partial class AppContext : System.Text.Json.Serialization.JsonSerializerContext { }
            public static class Program
            {
                public static void Main()
                {
                    if ((AppContext.Default.GetTypeInfo(typeof(Previous)) is not null) != {{(hasSourceMetadata ? "true" : "false")}})
                        throw new System.InvalidOperationException("Generated source metadata differs from expected reachability.");
                }
            }
            """;

        var result = await SdkAnalyzerFixture.BuildAsync(source, typeof(ContextSourceMetadataAnalyzer).Assembly.Location, TestContext.Current.CancellationToken, execute: true);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(warningCount, GetDiagnostics(result.Diagnostics).Length);
    }

    [Theory]
    [InlineData("public Previous Snapshot { get; set; }")]
    [InlineData("public Previous Snapshot;")]
    public async Task Sdk_inherited_public_members_supply_source_metadata(string member)
    {
        var result = await SdkAnalyzerFixture.BuildAsync(SdkContracts + $$"""
            public class Base { {{member}} }
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Base, Egil.SystemTextJson.Migration.IMigrateFrom<Previous, Current> { }
            public class Previous { }
            [System.Text.Json.Serialization.JsonSerializable(typeof(Current))]
            public partial class AppContext : System.Text.Json.Serialization.JsonSerializerContext { }
            public static class Program
            {
                public static void Main()
                {
                    if (AppContext.Default.GetTypeInfo(typeof(Previous)) is null)
                        throw new System.InvalidOperationException("Inherited member source metadata was not generated.");
                }
            }
            """, typeof(ContextSourceMetadataAnalyzer).Assembly.Location, TestContext.Current.CancellationToken, execute: true);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Empty(GetDiagnostics(result.Diagnostics));
    }

    [Fact]
    public async Task Sdk_ignored_override_does_not_generate_base_property_metadata()
    {
        var result = await SdkAnalyzerFixture.BuildAsync(SdkContracts + """
            public class Base { public virtual Previous Snapshot { get; set; } }
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Base, Egil.SystemTextJson.Migration.IMigrateFrom<Previous, Current>
            {
                [System.Text.Json.Serialization.JsonIgnore] public override Previous Snapshot { get; set; }
            }
            public class Previous { }
            [System.Text.Json.Serialization.JsonSerializable(typeof(Current))]
            public partial class AppContext : System.Text.Json.Serialization.JsonSerializerContext { }
            public static class Program
            {
                public static void Main()
                {
                    if (AppContext.Default.GetTypeInfo(typeof(Previous)) is not null)
                        throw new System.InvalidOperationException("Ignored override generated source metadata.");
                }
            }
            """, typeof(ContextSourceMetadataAnalyzer).Assembly.Location, TestContext.Current.CancellationToken, execute: true);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Single(GetDiagnostics(result.Diagnostics));
    }

    [Theory]
    [InlineData("Wrapper", "")]
    [InlineData("Current[]", "")]
    [InlineData("Wrapper", "[System.Text.Json.Serialization.JsonSerializable(typeof(Current))]")]
    public async Task Sdk_transitive_target_missing_source_reports_once(string root, string extraRegistration)
    {
        var result = await SdkAnalyzerFixture.BuildAsync(SdkContracts + $$"""
            public class Wrapper { public Current Value { get; set; } }
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Previous, Current> { }
            public class Previous { }
            [System.Text.Json.Serialization.JsonSerializable(typeof({{root}}))]
            {{extraRegistration}}
            public partial class AppContext : System.Text.Json.Serialization.JsonSerializerContext { }
            public static class Program
            {
                public static void Main()
                {
                    if (AppContext.Default.GetTypeInfo(typeof(Current)) is null || AppContext.Default.GetTypeInfo(typeof(Previous)) is not null)
                        throw new System.InvalidOperationException("Expected only transitive target metadata.");
                }
            }
            """, typeof(ContextSourceMetadataAnalyzer).Assembly.Location, TestContext.Current.CancellationToken, execute: true);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Single(GetDiagnostics(result.Diagnostics));
    }
#endif

    private static System.Text.Json.JsonElement[] GetDiagnostics(string diagnostics)
    {
        using var log = System.Text.Json.JsonDocument.Parse(diagnostics);
        return log.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Where(diagnostic => diagnostic.GetProperty("ruleId").GetString() == "STJM0006")
            .Select(diagnostic => diagnostic.Clone()).ToArray();
    }
}
