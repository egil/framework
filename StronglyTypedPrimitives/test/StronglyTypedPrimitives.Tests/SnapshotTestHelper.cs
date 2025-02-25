using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StronglyTypedPrimitives;

public static partial class SnapshotTestHelper
{
    public static (ImmutableArray<Diagnostic> Diagnostics, string Output) GetGeneratedOutput<T>(
        string source,
        bool includeAttributes = true)
        where T : IIncrementalGenerator, new()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(T).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.ComponentModel.DataAnnotations.ValidationAttribute).Assembly.Location)
        };

        references.AddRange(
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(assembly => MetadataReference.CreateFromFile(assembly.Location)));

        var compilation = CSharpCompilation.Create(
            "generator",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var originalTreeCount = compilation.SyntaxTrees.Length;

        CSharpGeneratorDriver
            .Create(new T())
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        // If we don't want the attributes, we try to get all the potential resource streams and exclude them
        var countsToExclude = includeAttributes
            ? originalTreeCount
            : originalTreeCount + 2;

        var generatedTrees = outputCompilation.SyntaxTrees.Skip(countsToExclude).ToList();

        // Include both source and generated code in compilation
        var allTrees = new[] { syntaxTree }.Concat(outputCompilation.SyntaxTrees.Skip(originalTreeCount)).ToList();

        // Validate the generated code compiles with source included
        var generatedCompilation = CSharpCompilation.Create(
            "generated",
            allTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generatedDiagnostics = generatedCompilation.GetDiagnostics();
        var allDiagnostics = diagnostics.AddRange(generatedDiagnostics);

        var output = string.Join("\n", generatedTrees.Select(t => t.ToString()));
        return (allDiagnostics, output);
    }
}
