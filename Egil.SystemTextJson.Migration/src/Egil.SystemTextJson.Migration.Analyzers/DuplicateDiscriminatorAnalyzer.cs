using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Egil.SystemTextJson.Migration.Analyzers;

/// <summary>
/// Detects migration contracts that assign the same discriminator to one target.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateDiscriminatorAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor DuplicateSourceDiscriminator = DiagnosticDescriptorFactory.Create(
        "STJM0001", "Duplicate migration source discriminator",
        "Source type '{0}' uses discriminator '{1}' and property '{2}' already used for target '{3}'", DiagnosticSeverity.Warning);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(DuplicateSourceDiscriminator);

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

            var selectorAttributes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var builder = start.Compilation.GetTypeByMetadataName("Egil.SystemTextJson.Migration.JsonMigrationBuilder");
            // Callback-selected discriminators depend on runtime attribute values, so only
            // sources carrying that callback's attribute are unknown.
            start.RegisterSyntaxNodeAction(node =>
            {
                if (builder is not null && node.Node is Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax invocation
                    && node.SemanticModel.GetSymbolInfo(invocation, node.CancellationToken).Symbol is IMethodSymbol method
                    && SymbolEqualityComparer.Default.Equals(method.ContainingType, builder))
                {
                    if (method.Name == "GetTypeDiscriminatorFrom" && method.TypeArguments[0] is INamedTypeSymbol attribute)
                    {
                        lock (selectorAttributes)
                        {
                            selectorAttributes.Add(attribute);
                        }
                    }
                }
            }, Microsoft.CodeAnalysis.CSharp.SyntaxKind.InvocationExpression);
            start.RegisterCompilationEndAction(end =>
            {
                AnalyzeCompilation(end, symbols, selectorAttributes);
            });
        });
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context, MigrationSymbols symbols, HashSet<INamedTypeSymbol> selectorAttributes)
    {
        var claimed = new Dictionary<(ITypeSymbol Target, string Property, string Discriminator), ClaimedSources>(new DiscriminatorKeyComparer());
        var visitedContracts = new HashSet<(ITypeSymbol Source, ITypeSymbol Target)>(new SourceTargetComparer());
        foreach (var type in GetTypes(context.Compilation.Assembly.GlobalNamespace))
        {
            foreach (var contract in type.AllInterfaces)
            {
                if (!SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, symbols.Migrate) && !SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, symbols.MigrateFrom))
                {
                    continue;
                }

                if (contract.TypeArguments[1] is not INamedTypeSymbol target
                    || GetAttribute(target, symbols.JsonMigratableAttribute, inherit: true) is null)
                {
                    continue;
                }

                var declaredSource = contract.TypeArguments[0];
                // Arrays and unresolved Nullable<T> contracts have no named metadata source
                // that this rule can compare. Keep the declared source for contract identity.
                if (NormalizeSource(declaredSource) is not { } source
                    || !visitedContracts.Add((declaredSource, target)))
                {
                    continue;
                }

                var declaredAttribute = GetAttribute(source, symbols.JsonMigratableAttribute, inherit: false);
                var attribute = GetAttribute(source, symbols.JsonMigratableAttribute, inherit: true);
                if (source.IsGenericType && GetString(declaredAttribute, "TypeDiscriminator") is null)
                {
                    continue;
                }
                if (source.GetAttributes().Any(attribute => attribute.AttributeClass is INamedTypeSymbol sourceAttribute && selectorAttributes.Any(selector => IsAssignableTo(sourceAttribute, selector))))
                {
                    continue;
                }
                var discriminator = GetString(declaredAttribute, "TypeDiscriminator") ?? GetRuntimeFullName(source);
                var configuredProperty = GetString(attribute, "TypeDiscriminatorPropertyName");
                var property = configuredProperty ?? "$type";
                var key = (contract.TypeArguments[1], property, discriminator);
                if (claimed.TryGetValue(key, out var claimedSources))
                {
                    var existingSource = configuredProperty is null ? claimedSources.DefaultPropertySource : claimedSources.ExplicitPropertySource;
                    if (existingSource is not null)
                    {
                        var location = attribute?.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                            ?? source.Locations.FirstOrDefault(candidate => candidate.IsInSource)
                            ?? type.Locations.FirstOrDefault(candidate => candidate.IsInSource)
                            ?? target.Locations.FirstOrDefault(candidate => candidate.IsInSource)
                            ?? Location.None;
                        context.ReportDiagnostic(Diagnostic.Create(DuplicateSourceDiscriminator, location, source.Name, discriminator, configuredProperty ?? "configured default", contract.TypeArguments[1].Name));
                    }
                    else if (configuredProperty is null)
                    {
                        claimed[key] = new ClaimedSources(claimedSources.ExplicitPropertySource, source);
                    }
                    else
                    {
                        claimed[key] = new ClaimedSources(source, claimedSources.DefaultPropertySource);
                    }
                }
                else
                {
                    claimed.Add(key, configuredProperty is null ? new ClaimedSources(null, source) : new ClaimedSources(source, null));
                }
            }
        }
    }

    private static string? GetString(AttributeData? attribute, string name) => attribute?.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value as string;

    private static INamedTypeSymbol? NormalizeSource(ITypeSymbol source) => source is INamedTypeSymbol named
        ? named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ? named.TypeArguments[0] as INamedTypeSymbol : named
        : null;

    private static bool IsAssignableTo(INamedTypeSymbol type, INamedTypeSymbol target)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, target)) return true;
        }
        return type.AllInterfaces.Any(@interface => SymbolEqualityComparer.Default.Equals(@interface, target));
    }

    private static AttributeData? GetAttribute(INamedTypeSymbol type, INamedTypeSymbol attribute, bool inherit)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = inherit ? current.BaseType : null)
        {
            var result = current.GetAttributes().FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attribute));
            if (result is not null)
            {
                return result;
            }
        }
        return null;
    }

    private static string GetRuntimeFullName(INamedTypeSymbol type)
    {
        var name = type.MetadataName;
        for (var containing = type.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            name = containing.MetadataName + "+" + name;
        }
        return type.ContainingNamespace.IsGlobalNamespace ? name : type.ContainingNamespace.ToDisplayString() + "." + name;
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

    private sealed class DiscriminatorKeyComparer : IEqualityComparer<(ITypeSymbol Target, string Property, string Discriminator)>
    {
        public bool Equals((ITypeSymbol Target, string Property, string Discriminator) x, (ITypeSymbol Target, string Property, string Discriminator) y) => SymbolEqualityComparer.Default.Equals(x.Target, y.Target) && x.Property == y.Property && x.Discriminator == y.Discriminator;
        public int GetHashCode((ITypeSymbol Target, string Property, string Discriminator) value)
        {
            unchecked
            {
                return ((SymbolEqualityComparer.Default.GetHashCode(value.Target) * 397) ^ value.Property.GetHashCode()) * 397 ^ value.Discriminator.GetHashCode();
            }
        }
    }

    private sealed class ClaimedSources
    {
        public ClaimedSources(INamedTypeSymbol? explicitPropertySource, INamedTypeSymbol? defaultPropertySource)
        {
            ExplicitPropertySource = explicitPropertySource;
            DefaultPropertySource = defaultPropertySource;
        }

        public INamedTypeSymbol? ExplicitPropertySource { get; }

        public INamedTypeSymbol? DefaultPropertySource { get; }
    }

    private sealed class SourceTargetComparer : IEqualityComparer<(ITypeSymbol Source, ITypeSymbol Target)>
    {
        public bool Equals((ITypeSymbol Source, ITypeSymbol Target) x, (ITypeSymbol Source, ITypeSymbol Target) y) => SymbolEqualityComparer.Default.Equals(x.Source, y.Source) && SymbolEqualityComparer.Default.Equals(x.Target, y.Target);
        public int GetHashCode((ITypeSymbol Source, ITypeSymbol Target) value) => SymbolEqualityComparer.Default.GetHashCode(value.Source) * 397 ^ SymbolEqualityComparer.Default.GetHashCode(value.Target);
    }
}
