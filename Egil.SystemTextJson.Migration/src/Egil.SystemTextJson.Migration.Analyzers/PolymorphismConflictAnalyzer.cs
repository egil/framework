using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Egil.SystemTextJson.Migration.Analyzers;

/// <summary>
/// Detects migration types that conflict with System.Text.Json polymorphism.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PolymorphismConflictAnalyzer : DiagnosticAnalyzer
{
    private const string PolymorphismHelpLink = "https://github.com/egil/framework/blob/main/Egil.SystemTextJson.Migration/docs/recipes/polymorphism.md";

    private static readonly DiagnosticDescriptor JsonMigratablePolymorphismConflict = DiagnosticDescriptorFactory.Create(
        id: "STJM0005",
        title: "JsonMigratable conflicts with System.Text.Json polymorphism",
        messageFormat: "'{0}' uses JsonMigratable in a System.Text.Json polymorphic hierarchy",
        defaultSeverity: DiagnosticSeverity.Warning,
        helpLinkUri: PolymorphismHelpLink);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(JsonMigratablePolymorphismConflict);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static compilationContext =>
        {
            if (!MigrationSymbols.TryCreate(compilationContext.Compilation, out var symbols)
                || symbols?.JsonPolymorphicAttribute is null)
            {
                return;
            }

            compilationContext.RegisterSymbolAction(symbolContext => AnalyzeNamedType(symbolContext, symbols), SymbolKind.NamedType);
        });
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context, MigrationSymbols symbols)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        var hasJsonMigratable = false;
        var hasStjPolymorphism = false;
        var declaresRelevantAttribute = false;

        for (INamedTypeSymbol? hierarchyType = type; hierarchyType is not null; hierarchyType = hierarchyType.BaseType)
        {
            foreach (var attribute in hierarchyType.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, symbols.JsonMigratableAttribute))
                {
                    hasJsonMigratable = true;
                    declaresRelevantAttribute |= SymbolEqualityComparer.Default.Equals(hierarchyType, type);
                }

                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, symbols.JsonPolymorphicAttribute) ||
                    SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, symbols.JsonDerivedTypeAttribute))
                {
                    hasStjPolymorphism = true;
                    declaresRelevantAttribute |= SymbolEqualityComparer.Default.Equals(hierarchyType, type);
                }
            }
        }

        foreach (var implementedInterface in type.AllInterfaces)
        {
            if (HasStjPolymorphism(implementedInterface, symbols))
            {
                hasStjPolymorphism = true;
            }
        }

        if (type.Interfaces.Any(implementedInterface =>
            HasStjPolymorphism(implementedInterface, symbols) ||
            implementedInterface.AllInterfaces.Any(@interface => HasStjPolymorphism(@interface, symbols))))
        {
            declaresRelevantAttribute = true;
        }

        if (declaresRelevantAttribute && hasJsonMigratable && hasStjPolymorphism)
        {
            context.ReportDiagnostic(Diagnostic.Create(JsonMigratablePolymorphismConflict, type.Locations[0], type.Name));
        }
    }

    private static bool HasStjPolymorphism(INamedTypeSymbol type, MigrationSymbols symbols) => type.GetAttributes().Any(attribute =>
        SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, symbols.JsonPolymorphicAttribute) ||
        SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, symbols.JsonDerivedTypeAttribute));
}
