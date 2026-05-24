using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.RegularExpressions;

namespace Egil.StronglyTypedPrimitives;

public static class SnapshotTestHelper
{
    public static Task Verify<TGenerator>(string source, out Compilation compilation)
        where TGenerator : IIncrementalGenerator, new()
        => Verify<TGenerator>(source, LanguageVersion.Preview, [], out compilation, out _, null);

    public static Task Verify<TGenerator>(string source, LanguageVersion languageVersion, out Compilation compilation)
        where TGenerator : IIncrementalGenerator, new()
        => Verify<TGenerator>(source, languageVersion, [], out compilation, out _, null);

    public static Task Verify<TGenerator>(
        string source,
        LanguageVersion languageVersion,
        out Compilation compilation,
        string? parameterText)
        where TGenerator : IIncrementalGenerator, new()
        => Verify<TGenerator>(source, languageVersion, [], out compilation, out _, parameterText);

    public static Task Verify<TGenerator>(
        string source,
        LanguageVersion languageVersion,
        IEnumerable<Type> includeTypesAssembly,
        out Compilation compilation,
        out List<byte[]> generatedAssemblies,
        string? parameterText = null)
        where TGenerator : IIncrementalGenerator, new()
    {
        var parseOptions = new CSharpParseOptions(languageVersion);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Concat(
            [
                MetadataReference.CreateFromFile(typeof(TGenerator).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(StronglyTypedAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonSerializer).Assembly.Location)
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

        var verification = Verifier.Verify(driver.RunGeneratorsAndUpdateCompilation(inputCompilation, out compilation, out var _))
            .ScrubLinesWithReplace(x => Regex.Replace(x, @"\d+\.\d+\.\d+\.\d+", "x.x.x.x"));

        if (!string.IsNullOrWhiteSpace(parameterText))
        {
            verification = verification.UseTextForParameters(parameterText);
        }

        return verification;
    }

    public static string GetParameterText(string typeName, LanguageVersion languageVersion)
        => $"underlyingType={GetTypeText(typeName)}_languageVersion={GetLanguageVersionText(languageVersion)}";

    private static string GetTypeText(string typeName)
        => typeName switch
        {
            "string" => "str",
            "int" => "i",
            "long" => "l",
            "decimal" => "dc",
            "double" => "d",
            "float" => "f",
            "byte" => "b",
            "bool" => "bool",
            "char" => "c",
            "System.Guid" => "guid",
            "System.DateTime" => "dt",
            "System.DateTimeOffset" => "dto",
            "System.TimeSpan" => "ts",
            "System.DateOnly" => "do",
            "System.TimeOnly" => "to",
            "System.String" => "str",
            _ => typeName
        };

    private static string GetLanguageVersionText(LanguageVersion languageVersion)
        => languageVersion switch
        {
            LanguageVersion.Preview => "pr",
            LanguageVersion.Latest => "la",
            LanguageVersion.LatestMajor => "lm",
            _ => GetLanguageVersionFallback(languageVersion)
        };

    private static string GetLanguageVersionFallback(LanguageVersion languageVersion)
    {
        var text = languageVersion.ToString();

        if (text.StartsWith("CSharp", System.StringComparison.Ordinal))
        {
            return $"c{text[6..].ToLowerInvariant()}";
        }

        return text;
    }
}
