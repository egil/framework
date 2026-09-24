using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Egil.SystemTextJson.Migration.Analyzers;

/// <summary>Detects target migrations that cannot distinguish non-object source payloads.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AmbiguousNonObjectSourceAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = DiagnosticDescriptorFactory.Create(
        "STJM0009",
        "Migration sources have an ambiguous JSON shape",
        "Migratable target '{0}' has multiple source types that deserialize from JSON {1} values without a discriminator",
        DiagnosticSeverity.Warning);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(static compilationContext =>
        {
            if (!MigrationSymbols.TryCreate(compilationContext.Compilation, out var symbols) || symbols is null)
            {
                return;
            }

            AnalyzeCompilation(compilationContext, symbols, new NonObjectSourceShapeClassifier(compilationContext.Compilation));
        });
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context, MigrationSymbols symbols, NonObjectSourceShapeClassifier shapes)
    {
        foreach (var target in GetTypes(context.Compilation.Assembly.GlobalNamespace))
        {
            var marker = GetMigrationMarker(target, symbols.JsonMigratableAttribute);
            if (marker is null)
            {
                continue;
            }

            var sourcesByShape = new Dictionary<string, (SourceShape Shape, HashSet<ITypeSymbol> Sources)>();
            foreach (var contract in target.AllInterfaces)
            {
                if (!SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, symbols.MigrateFrom)
                    || !SymbolEqualityComparer.Default.Equals(contract.TypeArguments[1], target))
                {
                    continue;
                }

                var shape = shapes.Classify(contract.TypeArguments[0], symbols.JsonMigratableAttribute);
                if (shape is null)
                {
                    continue;
                }

                if (!sourcesByShape.TryGetValue(shape.Key, out var entry))
                {
                    entry = (shape.Shape, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
                    sourcesByShape.Add(shape.Key, entry);
                }

                entry.Sources.Add(contract.TypeArguments[0]);
            }

            foreach (var entry in sourcesByShape)
            {
                var shape = entry.Value.Shape;
                var sources = entry.Value.Sources;
                if (sources.Count < 2)
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(Rule, target.Locations[0], target.Name, shape.ToString().ToLowerInvariant()));
            }
        }
    }

    private static AttributeData? GetMigrationMarker(INamedTypeSymbol type, INamedTypeSymbol marker)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            var attribute = current.GetAttributes().FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, marker));
            if (attribute is not null)
            {
                return attribute;
            }
        }

        return null;
    }

    private static IEnumerable<INamedTypeSymbol> GetTypes(INamespaceSymbol @namespace)
    {
        foreach (var type in @namespace.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in GetNestedTypes(type))
            {
                yield return nested;
            }
        }

        foreach (var child in @namespace.GetNamespaceMembers())
        {
            foreach (var type in GetTypes(child))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var descendant in GetNestedTypes(nested))
            {
                yield return descendant;
            }
        }
    }

}
