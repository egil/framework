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
        generatedAssemblies = [];

        var driver = RunGenerator<TGenerator>(source, languageVersion, includeTypesAssembly, out compilation);

        var verification = Verifier.Verify(driver)
            .ScrubLinesWithReplace(x => Regex.Replace(x, @"\d+\.\d+\.\d+\.\d+", "x.x.x.x"));

        if (!string.IsNullOrWhiteSpace(parameterText))
        {
            verification = verification.UseTextForParameters(parameterText);
        }

        return verification;
    }

    /// <summary>
    /// Runs the generator over <paramref name="source"/> and returns the run result, which carries
    /// the diagnostics the generator reported (as opposed to the compilation's own diagnostics).
    /// </summary>
    /// <param name="referenceAbstractions">
    /// When false the Abstractions assembly is left out of the compilation, which stands in for a
    /// consumer on its netstandard2.0 asset: System.Text.Json is referenced but none of the
    /// Abstractions types are, so the source must declare the [StronglyTyped] attribute itself.
    /// </param>
    public static GeneratorDriverRunResult RunGenerator<TGenerator>(string source, out Compilation compilation, bool referenceAbstractions = true)
        where TGenerator : IIncrementalGenerator, new()
        => RunGenerator<TGenerator>(source, LanguageVersion.LatestMajor, [], out compilation, referenceAbstractions).GetRunResult();

    /// <summary>
    /// Runs the generator against the framework reference assemblies this test project was
    /// compiled with, as recorded by the WriteReferenceAssemblyPaths target in the project file,
    /// instead of the implementation assemblies loaded in the test process. That is what a
    /// consumer's build compiles against, and reference assemblies leave out members that do not
    /// change a type's surface, such as an override that only changes behaviour, so a generator
    /// that looks for such members sees a different picture there.
    /// </summary>
    public static GeneratorDriverRunResult RunGeneratorAgainstFrameworkReferenceAssemblies<TGenerator>(string source, out Compilation compilation)
        where TGenerator : IIncrementalGenerator, new()
    {
        var referenceAssemblies = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "ReferenceAssemblies.txt"))
            .Where(path => path.Contains("microsoft.netcore.app.ref", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (referenceAssemblies.Count == 0)
        {
            throw new InvalidOperationException("No framework reference assemblies were recorded by the WriteReferenceAssemblyPaths target.");
        }

        var references = referenceAssemblies
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(StronglyTypedAttribute).Assembly.Location))
            .ToList();

        return RunGenerator<TGenerator>(source, LanguageVersion.LatestMajor, references, out compilation).GetRunResult();
    }

    private static GeneratorDriver RunGenerator<TGenerator>(
        string source,
        LanguageVersion languageVersion,
        IEnumerable<Type> includeTypesAssembly,
        out Compilation compilation,
        bool referenceAbstractions = true)
        where TGenerator : IIncrementalGenerator, new()
    {
        var abstractionsAssembly = typeof(StronglyTypedAttribute).Assembly;
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Where(assembly => referenceAbstractions || assembly != abstractionsAssembly)
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Concat(
            [
                MetadataReference.CreateFromFile(typeof(TGenerator).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonSerializer).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.ComponentModel.DataAnnotations.ValidationAttribute).Assembly.Location)
            ])
            .Concat(referenceAbstractions ? [MetadataReference.CreateFromFile(abstractionsAssembly.Location)] : [])
            .Concat(includeTypesAssembly.Select(x => MetadataReference.CreateFromFile(x.Assembly.Location)))
            .ToList();

        return RunGenerator<TGenerator>(source, languageVersion, references, out compilation);
    }

    private static GeneratorDriver RunGenerator<TGenerator>(
        string source,
        LanguageVersion languageVersion,
        IReadOnlyList<MetadataReference> references,
        out Compilation compilation)
        where TGenerator : IIncrementalGenerator, new()
    {
        var parseOptions = new CSharpParseOptions(languageVersion);
        var additionalTexts = new List<AdditionalText>();

        var inputCompilation = CSharpCompilation.Create("StronglyTypedPrimitivesSample",
            [CSharpSyntaxTree.ParseText(source, options: parseOptions, path: "Program.cs")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            generators: [new TGenerator().AsSourceGenerator()],
            additionalTexts: additionalTexts,
            parseOptions: parseOptions);

        return driver.RunGeneratorsAndUpdateCompilation(inputCompilation, out compilation, out var _);
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