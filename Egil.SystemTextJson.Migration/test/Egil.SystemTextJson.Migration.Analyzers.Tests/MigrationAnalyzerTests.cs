namespace Egil.SystemTextJson.Migration.Analyzers.Tests;

public sealed class MigrationAnalyzerTests
{
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
