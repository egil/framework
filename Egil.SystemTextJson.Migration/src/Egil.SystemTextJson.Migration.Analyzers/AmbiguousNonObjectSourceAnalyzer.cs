using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
        context.RegisterCompilationStartAction(static start =>
        {
            if (!MigrationSymbols.TryCreate(start.Compilation, out var symbols) || symbols is null)
            {
                return;
            }

            var builder = start.Compilation.GetTypeByMetadataName("Egil.SystemTextJson.Migration.JsonMigrationBuilder");
            var selectorAttributes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var hasBuilderPropertyOverride = false;
            if (builder is not null)
            {
                start.RegisterSyntaxNodeAction(node =>
                {
                    if (node.Node is not InvocationExpressionSyntax invocation
                        || node.SemanticModel.GetSymbolInfo(invocation, node.CancellationToken).Symbol is not IMethodSymbol method
                        || !SymbolEqualityComparer.Default.Equals(method.ContainingType, builder))
                    {
                        return;
                    }

                    lock (selectorAttributes)
                    {
                        if (method.Name == "GetTypeDiscriminatorFrom" && method.TypeArguments.FirstOrDefault() is INamedTypeSymbol attribute)
                        {
                            selectorAttributes.Add(attribute);
                        }
                        else if (method.Name == "SetTypeDiscriminatorPropertyName")
                        {
                            hasBuilderPropertyOverride = true;
                        }
                    }
                }, SyntaxKind.InvocationExpression);
            }

            start.RegisterCompilationEndAction(end => AnalyzeCompilation(end, symbols,
                new NonObjectSourceShapeClassifier(end.Compilation, selectorAttributes, hasBuilderPropertyOverride)));
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

            var sourcesByTier = new Dictionary<(SourceShape Shape, bool IsLegacy), List<(ITypeSymbol Source, ClassifiedShape Shape)>>();
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

                var tier = (shape.Shape, shape.IsLegacy);
                if (!sourcesByTier.TryGetValue(tier, out var sources))
                {
                    sources = new List<(ITypeSymbol Source, ClassifiedShape Shape)>();
                    sourcesByTier.Add(tier, sources);
                }

                var source = contract.TypeArguments[0];
                if (!sources.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.Source, source)))
                {
                    sources.Add((source, shape));
                }
            }

            foreach (var tier in sourcesByTier)
            {
                var sources = tier.Value;
                if (!sources.Where((candidate, index) => sources.Skip(index + 1)
                        .Any(other => candidate.Shape.CanCollideWith(other.Shape))).Any())
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(Rule, target.Locations[0], target.Name, tier.Key.Shape.ToString().ToLowerInvariant()));
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
