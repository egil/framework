using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;

namespace Egil.SystemTextJson.Migration.Analyzers;

/// <summary>Identifies migratable targets whose JSON contract cannot carry a discriminator property.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnsupportedTargetKindAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = DiagnosticDescriptorFactory.Create(
        "STJM0004", "Migratable targets must serialize as JSON objects",
        "Migratable type '{0}' is a collection, dictionary, or union; use an object target that can carry a discriminator property",
        DiagnosticSeverity.Warning);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var marker = start.Compilation.GetTypeByMetadataName("Egil.SystemTextJson.Migration.JsonMigratableAttribute");
            if (marker is null)
            {
                return;
            }

            var enumerable = start.Compilation.GetTypeByMetadataName("System.Collections.IEnumerable");
            var asyncEnumerable = start.Compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerable`1");
            var union = start.Compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.UnionAttribute");
            start.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                if (!HasMigrationMarker(type, marker))
                {
                    return;
                }

                var isCollection = type.AllInterfaces.Any(contract =>
                    SymbolEqualityComparer.Default.Equals(contract, enumerable)
                    || SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, asyncEnumerable));
                var isUnion = union is not null && type.GetAttributes().Any(attribute =>
                    SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, union));
                // UnionAttribute is synthesized during emit, so source symbols may not expose it.
                // Roslyn 4.8 has no union syntax API: inspect the declaration keyword while
                // distinguishing an ordinary identifier named union. See issue #232.
                isUnion |= type.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(symbolContext.CancellationToken)
                    .ChildTokens().Any(token => token.Text == "union" && !token.IsKind(SyntaxKind.IdentifierToken)));
                if (isCollection || isUnion)
                {
                    symbolContext.ReportDiagnostic(Diagnostic.Create(Rule, type.Locations[0], type.Name));
                }
            }, SymbolKind.NamedType);
        });
    }

    private static bool HasMigrationMarker(INamedTypeSymbol type, INamedTypeSymbol marker)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.GetAttributes().Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker)))
            {
                return true;
            }
        }

        return false;
    }
}
