using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Egil.SystemTextJson.Migration.Analyzers;

/// <summary>
/// Reports targets whose undiscriminated source does not have a visible migrator contract.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UndiscriminatedSourceMigratorAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor MissingUndiscriminatedSourceMigrator = DiagnosticDescriptorFactory.Create(
        id: "STJM0003",
        title: "Undiscriminated source requires a migrator",
        messageFormat: "Target '{0}' configures undiscriminated source '{1}' without a matching static or visible external migrator contract",
        defaultSeverity: DiagnosticSeverity.Warning);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(MissingUndiscriminatedSourceMigrator);

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

            AnalyzeCompilation(compilationContext, symbols);
        });
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context, MigrationSymbols symbols)
    {
        var types = GetTypes(context.Compilation.Assembly.GlobalNamespace).ToArray();
        foreach (var target in types)
        {
            var attribute = GetJsonMigratableAttribute(target, symbols, out bool inherited);
            var source = GetUndiscriminatedSource(attribute);
            if (source is null || HasStaticMigrator(target, source, symbols) || HasVisibleExternalMigrator(types, target, source, symbols))
            {
                continue;
            }

            var location = inherited ? target.Locations[0] : attribute!.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? target.Locations[0];
            context.ReportDiagnostic(Diagnostic.Create(MissingUndiscriminatedSourceMigrator, location, target.Name, source.ToDisplayString()));
        }
    }

    private static AttributeData? GetJsonMigratableAttribute(INamedTypeSymbol type, MigrationSymbols symbols, out bool inherited)
    {
        for (INamedTypeSymbol? currentType = type; currentType is not null; currentType = currentType.BaseType)
        {
            foreach (var attribute in currentType.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, symbols.JsonMigratableAttribute))
                {
                    inherited = !SymbolEqualityComparer.Default.Equals(currentType, type);
                    return attribute;
                }
            }
        }

        inherited = false;
        return null;
    }

    private static ITypeSymbol? GetUndiscriminatedSource(AttributeData? attribute)
    {
        if (attribute is null)
        {
            return null;
        }

        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "UndiscriminatedSourceType")
            {
                return argument.Value.Value as ITypeSymbol;
            }
        }

        return null;
    }

    private static bool HasStaticMigrator(INamedTypeSymbol target, ITypeSymbol source, MigrationSymbols symbols)
    {
        foreach (var contract in target.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, symbols.MigrateFrom)
                && SymbolEqualityComparer.Default.Equals(contract.TypeArguments[0], source)
                && SymbolEqualityComparer.Default.Equals(contract.TypeArguments[1], target))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasVisibleExternalMigrator(IEnumerable<INamedTypeSymbol> types, INamedTypeSymbol target, ITypeSymbol source, MigrationSymbols symbols)
    {
        foreach (var migrator in types)
        {
            if (SymbolEqualityComparer.Default.Equals(migrator, target))
            {
                continue;
            }

            foreach (var contract in migrator.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, symbols.Migrate)
                    && SymbolEqualityComparer.Default.Equals(contract.TypeArguments[0], source)
                    && SymbolEqualityComparer.Default.Equals(contract.TypeArguments[1], target))
                {
                    // Registration can be supplied from another composition module, so a visible
                    // external contract is enough to avoid claiming that this target has no migrator.
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> GetTypes(INamespaceSymbol @namespace)
    {
        foreach (var type in @namespace.GetTypeMembers())
        {
            yield return type;
            foreach (var nestedType in GetNestedTypes(type))
            {
                yield return nestedType;
            }
        }

        foreach (var childNamespace in @namespace.GetNamespaceMembers())
        {
            foreach (var type in GetTypes(childNamespace))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nestedType in type.GetTypeMembers())
        {
            yield return nestedType;
            foreach (var descendant in GetNestedTypes(nestedType))
            {
                yield return descendant;
            }
        }
    }
}
