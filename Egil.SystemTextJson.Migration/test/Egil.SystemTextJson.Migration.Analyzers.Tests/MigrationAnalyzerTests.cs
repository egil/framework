namespace Egil.SystemTextJson.Migration.Analyzers.Tests;

public sealed class MigrationAnalyzerTests
{
    [Fact]
    public async Task Nonmatching_source_and_target_migrator_pairs_still_report_STJM0003()
    {
        const string contracts = """
            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { public System.Type? UndiscriminatedSourceType { get; set; } }
            }
            """;
        const string targets = """
            [Egil.SystemTextJson.Migration.JsonMigratable(UndiscriminatedSourceType = typeof(SourceA))]
            public sealed class Target : Egil.SystemTextJson.Migration.IMigrateFrom<SourceB, Target> { }
            public sealed class SourceA { }
            public sealed class SourceB { }
            public sealed class OtherTarget { }
            public sealed class External : Egil.SystemTextJson.Migration.IMigrate<SourceA, OtherTarget> { }
            """;
        var compilation = AnalyzerTestHelper.CreateCompilation(contracts, targets);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new UndiscriminatedSourceMigratorAnalyzer());

        Assert.Equal("STJM0003", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task Inherited_undiscriminated_source_without_derived_contract_reports_on_derived_type()
    {
        const string baseSource = """
            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                [System.AttributeUsage(System.AttributeTargets.Class, Inherited = true)]
                public sealed class JsonMigratableAttribute : System.Attribute { public System.Type? UndiscriminatedSourceType { get; set; } }
            }

            public sealed class Source { }
            [Egil.SystemTextJson.Migration.JsonMigratable(UndiscriminatedSourceType = typeof(Source))]
            public class Base : Egil.SystemTextJson.Migration.IMigrateFrom<Source, Base> { }
            """;
        const string derivedSource = "public sealed class Derived : Base { }";
        var compilation = AnalyzerTestHelper.CreateCompilation(baseSource, derivedSource);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new UndiscriminatedSourceMigratorAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("STJM0003", diagnostic.Id);
        Assert.Equal(derivedSource, (await diagnostic.Location.SourceTree!.GetTextAsync(TestContext.Current.CancellationToken)).ToString());
        Assert.Equal("Derived", derivedSource.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
    }

    [Fact]
    public async Task Undiscriminated_source_without_static_or_visible_external_migrator_reports_STJM0003()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { public System.Type? UndiscriminatedSourceType { get; set; } }
            }

            [Egil.SystemTextJson.Migration.JsonMigratable(UndiscriminatedSourceType = typeof(Source))]
            public sealed class Target { }

            public sealed class Source { }
            """);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new UndiscriminatedSourceMigratorAnalyzer());

        Assert.Equal("STJM0003", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task Matching_static_or_visible_external_migrator_does_not_report_STJM0003()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { public System.Type? UndiscriminatedSourceType { get; set; } }
            }

            [Egil.SystemTextJson.Migration.JsonMigratable(UndiscriminatedSourceType = typeof(StaticSource))]
            public sealed class StaticTarget : Egil.SystemTextJson.Migration.IMigrateFrom<StaticSource, StaticTarget> { }

            [Egil.SystemTextJson.Migration.JsonMigratable(UndiscriminatedSourceType = typeof(ExternalSource))]
            public sealed class ExternalTarget { }

            public sealed class ExternalMigrator : Egil.SystemTextJson.Migration.IMigrate<ExternalSource, ExternalTarget> { }
            public sealed class StaticSource { }
            public sealed class ExternalSource { }
            """);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new UndiscriminatedSourceMigratorAnalyzer());

        Assert.Empty(diagnostics);
    }
    [Fact]
    public async Task Json_migratable_target_implementing_external_migrator_contract_reports_STJM0002_on_the_contract()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { }
            }

            [Egil.SystemTextJson.Migration.JsonMigratable]
            public sealed class Target : Egil.SystemTextJson.Migration.IMigrate<Source, Target> { }

            public sealed class Source { }
            """);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new MigrationAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("STJM0002", diagnostic.Id);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, diagnostic.Severity);
        var sourceText = await diagnostic.Location.SourceTree!.GetTextAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Egil.SystemTextJson.Migration.IMigrate<Source, Target>", sourceText.ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public async Task Direct_external_migrator_contract_diagnostic_uses_the_target_base_list_location()
    {
        const string contracts = """
            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { }
            }
            """;
        const string target = """
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public sealed class Target : Egil.SystemTextJson.Migration.IMigrate<Source, Target> { }

            public sealed class Source { }
            """;
        var compilation = AnalyzerTestHelper.CreateCompilation(contracts, target);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new MigrationAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        var sourceText = await diagnostic.Location.SourceTree!.GetTextAsync(TestContext.Current.CancellationToken);

        Assert.Equal(target, sourceText.ToString());
        Assert.Equal("Egil.SystemTextJson.Migration.IMigrate<Source, Target>", sourceText.ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public async Task External_migrator_and_target_migrate_from_contract_do_not_report_STJM0002()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { }
            }

            [Egil.SystemTextJson.Migration.JsonMigratable]
            public sealed class Target : Egil.SystemTextJson.Migration.IMigrateFrom<Source, Target> { }

            public sealed class ExternalMigrator : Egil.SystemTextJson.Migration.IMigrate<Source, Target> { }
            public sealed class Source { }
            """);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new MigrationAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Json_migratable_type_implementing_external_migrator_for_another_target_reports_STJM0002()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { }
            }

            [Egil.SystemTextJson.Migration.JsonMigratable]
            public sealed class Target : Egil.SystemTextJson.Migration.IMigrate<Source, OtherTarget> { }

            public sealed class Source { }
            public sealed class OtherTarget { }
            """);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new MigrationAnalyzer());

        Assert.Equal("STJM0002", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task Json_migratable_target_inheriting_external_migrator_contract_reports_STJM0002_on_the_target()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { }
            }

            public class MigratableBase : Egil.SystemTextJson.Migration.IMigrate<Source, Target> { }

            [Egil.SystemTextJson.Migration.JsonMigratable]
            public sealed class Target : MigratableBase { }
            public sealed class Source { }
            """);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new MigrationAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        var sourceText = await diagnostic.Location.SourceTree!.GetTextAsync(TestContext.Current.CancellationToken);

        Assert.Equal("STJM0002", diagnostic.Id);
        Assert.Equal("Target", sourceText.ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public async Task Target_inheriting_json_migratable_marker_reports_STJM0002_for_direct_external_migrator_contract()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { }
            }

            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class MigratableBase { }

            public sealed class Target : MigratableBase, Egil.SystemTextJson.Migration.IMigrate<Source, Target> { }
            public sealed class Source { }
            """);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new MigrationAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        var sourceText = await diagnostic.Location.SourceTree!.GetTextAsync(TestContext.Current.CancellationToken);

        Assert.Equal("STJM0002", diagnostic.Id);
        Assert.Equal("Egil.SystemTextJson.Migration.IMigrate<Source, Target>", sourceText.ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public async Task Analyzer_loads_for_a_compilation_that_references_migration_contracts()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { }
            }
            """);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new MigrationAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Migration_symbols_are_available_when_all_contracts_are_referenced()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { }
            }
            """);

        var wasCreated = MigrationSymbols.TryCreate(compilation, out var symbols);

        Assert.True(wasCreated);
        Assert.NotNull(symbols);
        Assert.Equal("IMigrate", symbols.Migrate.Name);
    }

    [Fact]
    public void Migration_symbols_are_unavailable_when_a_contract_is_missing()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
            }
            """);

        var wasCreated = MigrationSymbols.TryCreate(compilation, out var symbols);

        Assert.False(wasCreated);
        Assert.Null(symbols);
    }

    [Fact]
    public void Diagnostic_descriptor_links_to_the_existing_diagnostics_recipe()
    {
        var descriptor = DiagnosticDescriptorFactory.Create("STJM0001", "Title", "Message", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);

        Assert.Equal("https://github.com/egil/framework/blob/main/Egil.SystemTextJson.Migration/docs/recipes/error-diagnostics.md", descriptor.HelpLinkUri);
    }
}
