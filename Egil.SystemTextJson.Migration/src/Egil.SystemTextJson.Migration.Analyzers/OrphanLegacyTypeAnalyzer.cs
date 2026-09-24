using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Egil.SystemTextJson.Migration.Analyzers;

/// <summary>Reports historical payload types that have no visible migration source contract.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OrphanLegacyTypeAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = DiagnosticDescriptorFactory.Create(
        "STJM0011",
        "Legacy payload type has no visible migration",
        "Legacy payload type '{0}' has no visible migration source contract. Set MigratedExternally = true when its migrator is in another assembly, or remove the marker and consider deleting the type if no migration remains.",
        DiagnosticSeverity.Warning);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(startContext =>
        {
            var marker = startContext.Compilation.GetTypeByMetadataName("Egil.SystemTextJson.Migration.JsonMigrationLegacyTypeAttribute");
            var migrate = startContext.Compilation.GetTypeByMetadataName("Egil.SystemTextJson.Migration.IMigrate`2");
            var migrateFrom = startContext.Compilation.GetTypeByMetadataName("Egil.SystemTextJson.Migration.IMigrateFrom`2");
            if (marker is null || migrate is null || migrateFrom is null)
            {
                return;
            }

            var visibleSources = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var type in GetTypes(startContext.Compilation.Assembly.GlobalNamespace))
            {
                foreach (var contract in type.AllInterfaces)
                {
                    if ((SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, migrate)
                        || SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, migrateFrom))
                        && contract.TypeArguments[0] is INamedTypeSymbol source)
                    {
                        visibleSources.Add(source.OriginalDefinition);
                    }
                }
            }

            startContext.RegisterSymbolAction(symbolContext => Analyze(symbolContext, marker, visibleSources), SymbolKind.NamedType);
        });
    }

    private static void Analyze(
        SymbolAnalysisContext context,
        INamedTypeSymbol marker,
        HashSet<INamedTypeSymbol> visibleSources)
    {
        var legacyType = (INamedTypeSymbol)context.Symbol;
        var markerAttribute = legacyType.GetAttributes().FirstOrDefault(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker));
        if (markerAttribute is null || IsMigratedExternally(markerAttribute) || visibleSources.Contains(legacyType.OriginalDefinition))
        {
            return;
        }

        var location = markerAttribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? legacyType.Locations[0];
        context.ReportDiagnostic(Diagnostic.Create(Rule, location, legacyType.ToDisplayString()));
    }

    private static bool IsMigratedExternally(AttributeData markerAttribute) =>
        markerAttribute.NamedArguments.Any(argument => argument.Key == "MigratedExternally" && argument.Value.Value is true);

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
