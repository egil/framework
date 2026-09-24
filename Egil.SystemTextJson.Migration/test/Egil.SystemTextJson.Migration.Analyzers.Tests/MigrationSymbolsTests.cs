using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Egil.SystemTextJson.Migration.Analyzers.Tests;

public sealed class MigrationSymbolsTests
{
    [Fact]
    public async Task Missing_System_Text_Json_does_not_disable_STJM0002()
    {
        const string source = """
            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { }
            }

            [Egil.SystemTextJson.Migration.JsonMigratable]
            public sealed class Target : Egil.SystemTextJson.Migration.IMigrate<Source, Target> { }

            public sealed class Source { }
            """;
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Where(static path => !Path.GetFileName(path).Equals("System.Text.Json.dll", StringComparison.OrdinalIgnoreCase))
            .Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            assemblyName: "WithoutSystemTextJson",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview), cancellationToken: TestContext.Current.CancellationToken)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new MigrationAnalyzer());

        Assert.Equal("STJM0002", Assert.Single(diagnostics).Id);
    }
}
