using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.RegularExpressions;

namespace Egil.StronglyTypedPrimitives;

public static class SnapshotTestHelper
{
    public static Task Verify<TGenerator>(string source, out Compilation compilation)
        where TGenerator : IIncrementalGenerator, new()
        => Verify<TGenerator>(source, LanguageVersion.Preview, [], out compilation, out _);

    public static Task Verify<TGenerator>(string source, LanguageVersion languageVersion, out Compilation compilation)
        where TGenerator : IIncrementalGenerator, new()
        => Verify<TGenerator>(source, languageVersion, [], out compilation, out _);

    public static Task Verify<TGenerator>(string source, LanguageVersion languageVersion, IEnumerable<Type> includeTypesAssembly, out Compilation compilation, out List<byte[]> generatedAssemblies)
        where TGenerator : IIncrementalGenerator, new()
    {
        var parseOptions = new CSharpParseOptions(languageVersion);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Concat(
            [
                MetadataReference.CreateFromFile(typeof(TGenerator).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(StronglyTypedAttribute).Assembly.Location)
            ])
            .Concat(includeTypesAssembly.Select(x => MetadataReference.CreateFromFile(x.Assembly.Location)))
            .ToList();

        var additionalTexts = new List<AdditionalText>();
        generatedAssemblies = [];

        var inputCompilation = CSharpCompilation.Create("StronglyTypedPrimitivesSample",
            [CSharpSyntaxTree.ParseText(source, options: parseOptions, path: "Program.cs")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            generators: [new TGenerator().AsSourceGenerator()],
            additionalTexts: additionalTexts,
            parseOptions: parseOptions);

        return Verifier.Verify(driver.RunGeneratorsAndUpdateCompilation(inputCompilation, out compilation, out var _))
            .ScrubLinesWithReplace(x => Regex.Replace(x, @"\d+\.\d+\.\d+\.\d+", "x.x.x.x"))
            .UseDirectory("./snapshots");
    }
}