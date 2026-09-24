using Microsoft.CodeAnalysis;

namespace Egil.SystemTextJson.Migration.Analyzers.Tests;

public sealed class AmbiguousNonObjectSourceAnalyzerTests
{
    private const string Contracts = """
        namespace Egil.SystemTextJson.Migration
        {
            public interface IMigrate<TSource, TTarget> { }
            public interface IMigrateFrom<TSource, TTarget> { }
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, Inherited = true)]
            public sealed class JsonMigratableAttribute : System.Attribute { public string TypeDiscriminator { get; set; } public string TypeDiscriminatorPropertyName { get; set; } }
        }
        """;

#if NET11_0_OR_GREATER
    [Fact]
    public async Task Sdk_context_with_widened_numeric_sources_in_one_target_reports_STJM0009()
    {
        var result = await SdkAnalyzerFixture.BuildAsync(SdkContracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<System.Half, Current>, Egil.SystemTextJson.Migration.IMigrateFrom<System.Int128, Current> { }
            [System.Text.Json.Serialization.JsonSerializable(typeof(Current))]
            public partial class AppContext : System.Text.Json.Serialization.JsonSerializerContext { }
            """, typeof(AmbiguousNonObjectSourceAnalyzer).Assembly.Location, TestContext.Current.CancellationToken);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains(GetDiagnostics(result.Diagnostics), diagnostic => diagnostic.GetProperty("ruleId").GetString() == "STJM0009");
    }

    [Fact]
    public async Task Sdk_context_with_discriminated_collection_elements_does_not_report_STJM0009()
    {
        var result = await SdkAnalyzerFixture.BuildAsync(SdkContracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "first")]
            public class First { }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "second")]
            public class Second { }
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<First>, Current>, Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<Second>, Current> { }
            [System.Text.Json.Serialization.JsonSerializable(typeof(Current))]
            public partial class AppContext : System.Text.Json.Serialization.JsonSerializerContext { }
            """, typeof(AmbiguousNonObjectSourceAnalyzer).Assembly.Location, TestContext.Current.CancellationToken);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain(GetDiagnostics(result.Diagnostics), diagnostic => diagnostic.GetProperty("ruleId").GetString() == "STJM0009");
    }
#endif

    [Theory]
    [InlineData("int", "long", "number")]
    [InlineData("System.Half", "System.Int128", "number")]
    [InlineData("System.Collections.Generic.List<int>", "System.Collections.Generic.List<long>", "collection")]
    public async Task Sources_with_the_same_non_object_shape_report_STJM0009(string first, string second, string shape)
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + $$"""
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<{{first}}, Current>, Egil.SystemTextJson.Migration.IMigrateFrom<{{second}}, Current> { }
            """);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var diagnostic = Assert.Single(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new AmbiguousNonObjectSourceAnalyzer()));

        Assert.Equal("STJM0009", diagnostic.Id);
        Assert.Contains(shape, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Different_non_object_shapes_and_discriminated_object_sources_do_not_report_STJM0009()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class DifferentShapes : Egil.SystemTextJson.Migration.IMigrateFrom<int, DifferentShapes>, Egil.SystemTextJson.Migration.IMigrateFrom<string, DifferentShapes> { }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "first")]
            public class FirstObject { }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "second")]
            public class SecondObject { }
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class ObjectTarget : Egil.SystemTextJson.Migration.IMigrateFrom<FirstObject, ObjectTarget>, Egil.SystemTextJson.Migration.IMigrateFrom<SecondObject, ObjectTarget> { }
            """);

        Assert.Empty(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new AmbiguousNonObjectSourceAnalyzer()));
    }

    [Fact]
    public async Task Legacy_and_widened_primitive_sources_do_not_report_STJM0009()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<string, Current>, Egil.SystemTextJson.Migration.IMigrateFrom<System.Guid, Current> { }
            """);

        Assert.Empty(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new AmbiguousNonObjectSourceAnalyzer()));
    }

    [Fact]
    public async Task Collections_with_distinct_visible_element_discriminators_do_not_report_STJM0009()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "first")]
            public class First { }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "second")]
            public class Second { }
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<First>, Current>, Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<Second>, Current> { }
            """);

        Assert.Empty(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new AmbiguousNonObjectSourceAnalyzer()));
    }

    [Theory]
    [InlineData("System.Collections.Generic.IDictionary<string, int>", "System.Collections.Generic.IDictionary<string, long>", "dictionary")]
    [InlineData("System.Collections.Generic.IAsyncEnumerable<int>", "System.Collections.Generic.IAsyncEnumerable<long>", "collection")]
    [InlineData("System.Memory<int>", "System.ReadOnlyMemory<int>", "collection")]
    public async Task Direct_collection_contracts_are_classified_by_runtime_shape(string first, string second, string shape)
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + $$"""
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<{{first}}, Current>, Egil.SystemTextJson.Migration.IMigrateFrom<{{second}}, Current> { }
            """);

        var diagnostic = Assert.Single(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new AmbiguousNonObjectSourceAnalyzer()));

        Assert.Contains(shape, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inherited_marker_reports_at_the_migratable_target()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Base { }
            public class Current : Base, Egil.SystemTextJson.Migration.IMigrateFrom<int, Current>, Egil.SystemTextJson.Migration.IMigrateFrom<long, Current> { }
            """);

        var diagnostic = Assert.Single(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new AmbiguousNonObjectSourceAnalyzer()));
        var text = await diagnostic.Location.SourceTree!.GetTextAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Current", text.ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public async Task Collection_elements_with_distinct_default_discriminators_do_not_report_STJM0009()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class First { }
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Second { }
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<First>, Current>, Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<Second>, Current> { }
            """);

        Assert.Empty(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new AmbiguousNonObjectSourceAnalyzer()));
    }

    [Fact]
    public async Task Collection_elements_with_same_value_and_different_properties_do_not_report_STJM0009()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminatorPropertyName = "first", TypeDiscriminator = "same")]
            public class First { }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminatorPropertyName = "second", TypeDiscriminator = "same")]
            public class Second { }
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<First>, Current>, Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<Second>, Current> { }
            """);

        Assert.Empty(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new AmbiguousNonObjectSourceAnalyzer()));
    }

    [Fact]
    public async Task Inherited_element_property_name_participates_in_collection_disambiguation()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminatorPropertyName = "legacy")]
            public class LegacyBase { }
            public class First : LegacyBase { }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminatorPropertyName = "current")]
            public class Second { }
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<First>, Current>, Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<Second>, Current> { }
            """);

        Assert.Empty(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new AmbiguousNonObjectSourceAnalyzer()));
    }

    [Fact]
    public async Task Custom_converter_on_a_numeric_source_suppresses_STJM0009()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [System.Text.Json.Serialization.JsonConverter(typeof(LegacyConverter))]
            public enum Legacy { First }
            public sealed class LegacyConverter : System.Text.Json.Serialization.JsonConverter<Legacy>
            {
                public override Legacy Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options) => Legacy.First;
                public override void Write(System.Text.Json.Utf8JsonWriter writer, Legacy value, System.Text.Json.JsonSerializerOptions options) => writer.WriteStringValue("legacy");
            }
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Legacy, Current>, Egil.SystemTextJson.Migration.IMigrateFrom<int, Current> { }
            """);

        Assert.Empty(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new AmbiguousNonObjectSourceAnalyzer()));
    }

    [Theory]
    [InlineData("System.Collections.ArrayList", "System.Collections.Queue")]
    [InlineData("System.Collections.IEnumerable", "System.Collections.ArrayList")]
    public async Task Non_generic_collections_are_classified_as_collections(string first, string second)
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + $$"""
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<{{first}}, Current>, Egil.SystemTextJson.Migration.IMigrateFrom<{{second}}, Current> { }
            """);

        var diagnostic = Assert.Single(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new AmbiguousNonObjectSourceAnalyzer()));
        Assert.Contains("collection", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nullable_marked_struct_collection_elements_use_their_underlying_discriminators()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable] public struct First { }
            [Egil.SystemTextJson.Migration.JsonMigratable] public struct Second { }
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<First?>, Current>, Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<Second?>, Current> { }
            """);

        Assert.Empty(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new AmbiguousNonObjectSourceAnalyzer()));
    }

    [Fact]
    public async Task Constructed_generic_collection_elements_keep_their_type_arguments_in_default_discriminators()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable] public class Item<T> { }
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<Item<int>>, Current>, Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<Item<string>>, Current> { }
            """);

        Assert.Empty(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new AmbiguousNonObjectSourceAnalyzer()));
    }

    [Fact]
    public async Task Nested_constructed_generic_collection_elements_keep_containing_type_arguments_in_default_discriminators()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            public class Outer<T> { [Egil.SystemTextJson.Migration.JsonMigratable] public class Item { } }
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<Outer<int>.Item>, Current>, Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<Outer<string>.Item>, Current> { }
            """);

        Assert.Empty(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new AmbiguousNonObjectSourceAnalyzer()));
    }

    [Fact]
    public async Task Array_collection_elements_without_a_visible_discriminator_report_STJM0009()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<int[]>, Current>, Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<long[]>, Current> { }
            """);

        var diagnostic = Assert.Single(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new AmbiguousNonObjectSourceAnalyzer()));
        Assert.Equal("STJM0009", diagnostic.Id);
    }

    private const string SdkContracts = """
        namespace Egil.SystemTextJson.Migration
        {
            public interface IMigrate<TSource, TTarget> { }
            public interface IMigrateFrom<TSource, TTarget> { }
            [System.AttributeUsage(System.AttributeTargets.Class, Inherited = true)]
            public sealed class JsonMigratableAttribute : System.Attribute
            {
                public string? TypeDiscriminator { get; set; }
                public string? TypeDiscriminatorPropertyName { get; set; }
            }
        }
        """;

    private static System.Text.Json.JsonElement[] GetDiagnostics(string diagnostics)
    {
        using var log = System.Text.Json.JsonDocument.Parse(diagnostics);
        return log.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray().Select(static diagnostic => diagnostic.Clone()).ToArray();
    }
}
