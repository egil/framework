using Microsoft.CodeAnalysis;

namespace Egil.SystemTextJson.Migration.Analyzers.Tests;

public sealed class UnsupportedTargetKindAnalyzerTests
{
    private const string Contracts = """
        namespace Egil.SystemTextJson.Migration
        {
            public interface IMigrate<TSource, TTarget> { }
            public interface IMigrateFrom<TSource, TTarget> { }
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, Inherited = true)]
            public sealed class JsonMigratableAttribute : System.Attribute { }
        }
        """;

    [Theory]
    [InlineData("public class Target : System.Collections.Generic.List<int> { }")]
    [InlineData("public class Target : System.Collections.Generic.Dictionary<string, int> { }")]
    [InlineData("public class Target : System.Collections.Hashtable { }")]
    [InlineData("public struct Target : System.Collections.IEnumerable { public System.Collections.IEnumerator GetEnumerator() => null; }")]
    [InlineData("public class Target : System.Collections.Generic.IAsyncEnumerable<int> { public System.Collections.Generic.IAsyncEnumerator<int> GetAsyncEnumerator(System.Threading.CancellationToken cancellationToken = default) => null; }")]
    public async Task Marked_collection_or_dictionary_target_warns(string declaration)
    {
        var source = Contracts + "[Egil.SystemTextJson.Migration.JsonMigratable] " + declaration;
        var compilation = AnalyzerTestHelper.CreateCompilation(source);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new UnsupportedTargetKindAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("STJM0004", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Theory]
    [InlineData("public class Target { public System.Collections.Generic.List<int> Values { get; set; } }")]
    [InlineData("public struct Target { public int Value { get; set; } }")]
    [InlineData("public record Target(int Value);")]
    [InlineData("public class Target { public System.Collections.IEnumerator GetEnumerator() => null; }")]
    public async Task Marked_ordinary_object_target_does_not_warn(string declaration)
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + "[Egil.SystemTextJson.Migration.JsonMigratable] " + declaration);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new UnsupportedTargetKindAnalyzer());

        Assert.Empty(diagnostics);
    }
    [Fact]
    public async Task Unmarked_collections_and_foreign_marker_names_do_not_warn()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            public sealed class JsonMigratableAttribute : System.Attribute { }
            [JsonMigratable] public class Foreign : System.Collections.Generic.List<int> { }
            public class Unmarked : System.Collections.Generic.Dictionary<string, int> { }
            """);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new UnsupportedTargetKindAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Inherited_migration_marker_rejects_a_derived_collection()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable] public class Base { }
            public class Target : Base, System.Collections.IEnumerable
            {
                public System.Collections.IEnumerator GetEnumerator() => null;
            }
            """);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new UnsupportedTargetKindAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("STJM0004", diagnostic.Id);
        var text = await diagnostic.Location.SourceTree!.GetTextAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Target", text.ToString(diagnostic.Location.SourceSpan));
    }

#if NET11_0_OR_GREATER
    [Fact]
    public async Task Sdk_compiled_union_warns_while_object_targets_and_migratable_union_cases_do_not()
    {
        var result = await SdkAnalyzerFixture.BuildAsync(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable] public union InvalidUnion(int, string);
            [Egil.SystemTextJson.Migration.JsonMigratable] public class CurrentClass { }
            [Egil.SystemTextJson.Migration.JsonMigratable] public struct CurrentStruct { }
            public union ValidUnion(CurrentClass, CurrentStruct);
            [Egil.SystemTextJson.Migration.JsonMigratable] public class @union { }
            """, typeof(UnsupportedTargetKindAnalyzer).Assembly.Location, TestContext.Current.CancellationToken);

        Assert.True(result.ExitCode == 0, result.Output);
        using var log = System.Text.Json.JsonDocument.Parse(result.Diagnostics);
        var diagnostics = log.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray().ToArray();
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("STJM0004", diagnostic.GetProperty("ruleId").GetString());
        Assert.Contains("'InvalidUnion'", diagnostic.GetProperty("message").GetProperty("text").GetString(), StringComparison.Ordinal);
    }
#endif
}
