using Microsoft.CodeAnalysis;

namespace Egil.SystemTextJson.Migration.Analyzers.Tests;

public sealed class MissingStringMetadataAnalyzerTests
{
    private const string Contracts = """
        using System.Text.Json.Serialization;
        namespace Egil.SystemTextJson.Migration
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, Inherited = true)]
            public sealed class JsonMigratableAttribute : System.Attribute { }
        }
        """;

    [Fact]
    public async Task Numeric_migratable_target_without_string_metadata_warns_on_context()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable] public class Target { public int Value { get; set; } }
            [JsonSerializable(typeof(Target))] public abstract partial class AppContext : JsonSerializerContext { protected AppContext() : base(null) { } }
            """);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new MissingStringMetadataAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("STJM0007", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        var text = await diagnostic.Location.SourceTree!.GetTextAsync(TestContext.Current.CancellationToken);
        Assert.Equal("AppContext", text.ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public async Task Converter_owned_target_does_not_claim_missing_string_metadata()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable]
            [JsonConverter(typeof(TargetConverter))]
            public class Target { public int Value { get; set; } }
            public sealed class TargetConverter : JsonConverter<Target>
            {
                public override Target Read(ref System.Text.Json.Utf8JsonReader reader, System.Type type, System.Text.Json.JsonSerializerOptions options) => new();
                public override void Write(System.Text.Json.Utf8JsonWriter writer, Target value, System.Text.Json.JsonSerializerOptions options) => writer.WriteNumberValue(value.Value);
            }
            [JsonSerializable(typeof(Target))]
            public abstract partial class AppContext : JsonSerializerContext { protected AppContext() : base(null) { } }
            """);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        Assert.Empty(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new MissingStringMetadataAnalyzer()));
    }

    [Fact]
    public async Task Unbound_generic_registration_does_not_claim_missing_string_metadata()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable] public class Target { public int Value { get; set; } }
            public class Container<T> { public T Value { get; set; } }
            [JsonSerializable(typeof(Target)), JsonSerializable(typeof(Container<>))]
            public abstract partial class AppContext : JsonSerializerContext { protected AppContext() : base(null) { } }
            """);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        Assert.Empty(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new MissingStringMetadataAnalyzer()));
    }

    [Theory]
    [InlineData("public string Value { get; set; }", 0)]
    [InlineData("public string Value;", 0)]
    [InlineData("[JsonInclude] internal string Value;", 0)]
    [InlineData("[JsonInclude] private string Value { get; set; }", 0)]
    [InlineData("[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Value { get; set; }", 0)]
    [InlineData("public string[] Value { get; set; }", 0)]
    [InlineData("public System.ReadOnlyMemory<string> Value { get; set; }", 0)]
    [InlineData("public System.Collections.Generic.Dictionary<string,int> Value { get; set; }", 0)]
    [InlineData("public System.Collections.Generic.List<string> Value { get; set; }", 0)]
    [InlineData("[JsonIgnore] public string Value { get; set; }", 1)]
    [InlineData("[JsonIgnore(Condition = JsonIgnoreCondition.Always)] public string Value { get; set; }", 1)]
    [InlineData("private string Value;", 1)]
    [InlineData("public static string Value { get; set; }", 1)]
    [InlineData("public string this[int index] => null;", 1)]
    [InlineData("public int Value { get; set; }", 1)]
    [InlineData("public System.Guid Value { get; set; }", 1)]
    public async Task Only_reachable_string_metadata_satisfies_the_context(string member, int warningCount)
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + $$"""
            [Egil.SystemTextJson.Migration.JsonMigratable] public class Target { {{member}} }
            [JsonSerializable(typeof(Target))] public abstract partial class AppContext : JsonSerializerContext { protected AppContext() : base(null) { } }
            """);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new MissingStringMetadataAnalyzer());

        Assert.Equal(warningCount, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Equal("STJM0007", d.Id));
    }

    [Fact]
    public void Sdk_generated_contexts_confirm_public_fields_supply_string_metadata_but_ignored_members_do_not()
    {
        Assert.NotNull(FieldMetadataContext.Default.GetTypeInfo(typeof(string)));
        Assert.Null(IgnoredMetadataContext.Default.GetTypeInfo(typeof(string)));
    }

    [Theory]
    [InlineData("string", "")]
    [InlineData("Other", "public class Other { public string Name { get; set; } }")]
    [InlineData("Other", "public class Other : Base { } public class Base { public Nested Data { get; set; } } public class Nested { public string Name { get; set; } }")]
    [InlineData("Other", "public class Other { public Other Cycle { get; set; } public string Name { get; set; } }")]
    [InlineData("Other", "public interface Other : IBase { } public interface IBase { string Value { get; set; } }")]
    [InlineData("Other", "[JsonDerivedType(typeof(Child))] public class Other { } public class Child : Other { public string Value { get; set; } }")]
    [InlineData("Other", "public class Other : Box<string> { } public class Box<T> { public T Value { get; set; } }")]
    public async Task Explicit_or_transitive_string_metadata_in_another_registered_root_suppresses_warning(string root, string declaration)
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + $$"""
            [Egil.SystemTextJson.Migration.JsonMigratable] public class Target { }
            {{declaration}}
            [JsonSerializable(typeof(Target)), JsonSerializable(typeof({{root}}))]
            public abstract partial class AppContext : JsonSerializerContext { protected AppContext() : base(null) { } }
            """);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new MissingStringMetadataAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Nested_migratable_target_and_partial_context_report_once_and_other_context_string_does_not_help()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable] public class Base { }
            public class Target : Base { public Target Cycle { get; set; } }
            public class Wrapper { public Target Item { get; set; } }
            [JsonSerializable(typeof(Wrapper))] public abstract partial class Missing : JsonSerializerContext { protected Missing() : base(null) { } }
            [JsonSerializable(typeof(Target))] public abstract partial class Missing { }
            [JsonSerializable(typeof(string))] public abstract partial class Complete : JsonSerializerContext { protected Complete() : base(null) { } }
            """);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new MissingStringMetadataAnalyzer());

        Assert.Contains("'Missing'", Assert.Single(diagnostics).GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public void Sdk_generated_context_emits_string_metadata_even_for_inaccessible_included_member()
    {
        Assert.NotNull(PrivateIncludedMetadataContext.Default.GetTypeInfo(typeof(string)));
    }
#endif

    [Fact]
    public async Task Ignored_override_does_not_reintroduce_base_string_metadata()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            public class Base { public virtual string Name { get; set; } }
            [Egil.SystemTextJson.Migration.JsonMigratable] public class Target : Base
            {
                [JsonIgnore] public override string Name { get; set; }
            }
            [JsonSerializable(typeof(Target))] public abstract partial class AppContext : JsonSerializerContext { protected AppContext() : base(null) { } }
            """);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new MissingStringMetadataAnalyzer());

        Assert.Equal("STJM0007", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task Unmarked_roots_and_foreign_marker_do_not_require_string_metadata()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            public class JsonMigratableAttribute : System.Attribute { }
            [JsonMigratable] public class Target { public int Value { get; set; } }
            [Egil.SystemTextJson.Migration.JsonMigratable] public class Unregistered { }
            [JsonSerializable(typeof(Target))] public abstract partial class AppContext : JsonSerializerContext { protected AppContext() : base(null) { } }
            """);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new MissingStringMetadataAnalyzer());

        Assert.Empty(diagnostics);
    }
}

public sealed class FieldMetadataModel
{
    public string? Value;
}

public sealed class IgnoredMetadataModel
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Value { get; set; }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(FieldMetadataModel))]
internal partial class FieldMetadataContext : System.Text.Json.Serialization.JsonSerializerContext;

[System.Text.Json.Serialization.JsonSerializable(typeof(IgnoredMetadataModel))]
internal partial class IgnoredMetadataContext : System.Text.Json.Serialization.JsonSerializerContext;

#if NET11_0_OR_GREATER
public sealed class PrivateIncludedMetadataModel
{
    [System.Text.Json.Serialization.JsonInclude]
    private string? Value { get; set; }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(PrivateIncludedMetadataModel))]
internal partial class PrivateIncludedMetadataContext : System.Text.Json.Serialization.JsonSerializerContext;
#endif
