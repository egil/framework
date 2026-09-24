using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Egil.SystemTextJson.Migration.Analyzers;

/// <summary>
/// Provides the shared Roslyn registration point for migration diagnostics.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MigrationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor InvalidMigratableTarget = DiagnosticDescriptorFactory.Create(
        id: "STJM0002",
        title: "Migratable targets must use IMigrateFrom",
        messageFormat: "Type '{0}' is marked with [JsonMigratable] and cannot implement '{1}'. Implement IMigrateFrom<TSource, TTarget> on the target or move the IMigrate<TSource, TTarget> contract to an external migrator.",
        defaultSeverity: DiagnosticSeverity.Warning);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(InvalidMigratableTarget);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static compilationContext =>
        {
            if (!MigrationSymbols.TryCreate(compilationContext.Compilation, out var symbols) || symbols is null)
            {
                return;
            }

            compilationContext.RegisterSymbolAction(symbolContext => AnalyzeNamedType(symbolContext, symbols), SymbolKind.NamedType);
            compilationContext.RegisterSyntaxNodeAction(nodeContext => AnalyzeDirectBaseType(nodeContext, symbols), SyntaxKind.SimpleBaseType);
        });
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context, MigrationSymbols symbols)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (!HasJsonMigratableAttribute(type, symbols))
        {
            return;
        }

        foreach (var implementedInterface in type.AllInterfaces)
        {
            if (IsMigrationContract(implementedInterface, symbols) && !IsDirectlyImplemented(type, implementedInterface))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidMigratableTarget,
                    type.Locations[0],
                    type.Name,
                    implementedInterface.ToDisplayString()));
            }
        }
    }

    private static void AnalyzeDirectBaseType(SyntaxNodeAnalysisContext context, MigrationSymbols symbols)
    {
        var baseType = (BaseTypeSyntax)context.Node;
        if (baseType.Parent?.Parent is not TypeDeclarationSyntax declaration
            || context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not INamedTypeSymbol type
            || !HasJsonMigratableAttribute(type, symbols)
            || context.SemanticModel.GetTypeInfo(baseType.Type, context.CancellationToken).Type is not INamedTypeSymbol implementedInterface
            || !IsMigrationContract(implementedInterface, symbols))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            InvalidMigratableTarget,
            baseType.Type.GetLocation(),
            type.Name,
            implementedInterface.ToDisplayString()));
    }

    private static bool HasJsonMigratableAttribute(INamedTypeSymbol type, MigrationSymbols symbols)
    {
        for (INamedTypeSymbol? currentType = type; currentType is not null; currentType = currentType.BaseType)
        {
            foreach (var attribute in currentType.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, symbols.JsonMigratableAttribute))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsMigrationContract(INamedTypeSymbol implementedInterface, MigrationSymbols symbols) =>
        SymbolEqualityComparer.Default.Equals(implementedInterface.OriginalDefinition, symbols.Migrate);

    private static bool IsDirectlyImplemented(INamedTypeSymbol type, INamedTypeSymbol implementedInterface)
    {
        foreach (var directInterface in type.Interfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(directInterface, implementedInterface))
            {
                return true;
            }
        }

        return false;
    }
}
