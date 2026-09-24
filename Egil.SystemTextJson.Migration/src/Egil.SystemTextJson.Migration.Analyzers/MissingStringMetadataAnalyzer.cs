using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Egil.SystemTextJson.Migration.Analyzers;

/// <summary>Finds JSON contexts missing metadata for migration discriminator strings.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingStringMetadataAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = DiagnosticDescriptorFactory.Create(
        "STJM0007", "Migration discriminator requires string metadata",
        "JSON context '{0}' contains migratable types but lacks string metadata; add [JsonSerializable(typeof(string))]",
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
            var serializerContext = start.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonSerializerContext");
            var serializable = start.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonSerializableAttribute");
            if (marker is null || serializerContext is null || serializable is null)
            {
                return;
            }

            start.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                if (!Inherits(type, serializerContext))
                {
                    return;
                }

                var roots = type.GetAttributes().Where(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, serializable))
                    .Select(a => a.ConstructorArguments.FirstOrDefault().Value).OfType<ITypeSymbol>();
                var graph = new MetadataGraph(symbolContext, type, marker);
                foreach (var root in roots)
                {
                    graph.Visit(root);
                }

                if (graph.HasMigratableType && !graph.HasString && !graph.HasUnknownContract)
                {
                    symbolContext.ReportDiagnostic(Diagnostic.Create(Rule, type.Locations[0], type.Name));
                }
            }, SymbolKind.NamedType);
        });
    }

    private static bool Inherits(INamedTypeSymbol? type, INamedTypeSymbol expected)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, expected))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class MetadataGraph(SymbolAnalysisContext analysis, INamedTypeSymbol context, INamedTypeSymbol marker)
    {
        private readonly HashSet<ITypeSymbol> visited = new(SymbolEqualityComparer.Default);
        private readonly INamedTypeSymbol? ignore = analysis.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonIgnoreAttribute");
        private readonly INamedTypeSymbol? include = analysis.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonIncludeAttribute");
        private readonly INamedTypeSymbol? converter = analysis.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonConverterAttribute");
        private readonly INamedTypeSymbol? derivedType = analysis.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonDerivedTypeAttribute");
        private readonly INamedTypeSymbol? memory = analysis.Compilation.GetTypeByMetadataName("System.Memory`1");
        private readonly INamedTypeSymbol? readOnlyMemory = analysis.Compilation.GetTypeByMetadataName("System.ReadOnlyMemory`1");
        private readonly INamedTypeSymbol? constructor = analysis.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonConstructorAttribute");

        public bool HasString { get; private set; }
        public bool HasMigratableType { get; private set; }
        public bool HasUnknownContract { get; private set; }

        public void Visit(ITypeSymbol type, int depth = 0)
        {
            analysis.CancellationToken.ThrowIfCancellationRequested();
            if (HasString || HasUnknownContract || !visited.Add(type))
            {
                return;
            }

            if (type.SpecialType == SpecialType.System_String)
            {
                HasString = true;
                return;
            }

            // Open/expanding generic graphs and custom converters cannot be resolved reliably
            // from members alone. Avoid claiming missing metadata for those configurations.
            if (depth > 32 || type.TypeKind is TypeKind.TypeParameter or TypeKind.Error
                || type is INamedTypeSymbol { IsUnboundGenericType: true })
            {
                HasUnknownContract = true;
                return;
            }

            if (type is IArrayTypeSymbol array)
            {
                Visit(array.ElementType, depth + 1);
                return;
            }

            if (type is not INamedTypeSymbol named || type.SpecialType != SpecialType.None || type.TypeKind == TypeKind.Enum)
            {
                return;
            }

            for (var current = named; current is not null; current = current.BaseType)
            {
                HasMigratableType |= HasAttribute(current, marker);
            }

            if (named.GetAttributes().Any(a => converter is not null && Inherits(a.AttributeClass, converter)))
            {
                HasUnknownContract = true;
                return;
            }

            if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                Visit(named.TypeArguments[0], depth + 1);
                return;
            }

            if (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, memory)
                || SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, readOnlyMemory))
            {
                Visit(named.TypeArguments[0], depth + 1);
                return;
            }

            foreach (var attribute in named.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, derivedType)
                    && attribute.ConstructorArguments.FirstOrDefault().Value is ITypeSymbol derived)
                {
                    Visit(derived, depth + 1);
                }
            }

            var collection = new[] { named }.Concat(named.AllInterfaces).FirstOrDefault(i =>
                i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
            if (collection is not null)
            {
                Visit(collection.TypeArguments[0], depth + 1);
                return;
            }

            // Built-in scalar contracts do not generate metadata for their CLR properties.
            if (named.ContainingNamespace.ToDisplayString() == "System"
                && named.Name is "Guid" or "DateTimeOffset" or "TimeSpan" or "DateOnly" or "TimeOnly" or "Uri" or "Version")
            {
                return;
            }

            var hierarchy = new List<INamedTypeSymbol>();
            for (var current = named; current is not null; current = current.BaseType)
            {
                hierarchy.Add(current);
            }

            if (named.TypeKind == TypeKind.Interface)
            {
                hierarchy.AddRange(named.AllInterfaces);
            }

            var ignoredOverrides = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var current in hierarchy)
            {
                foreach (var member in current.GetMembers())
                {
                    if (member is IPropertySymbol ignoredProperty && IsIgnored(member))
                    {
                        for (var overridden = ignoredProperty.OverriddenProperty; overridden is not null; overridden = overridden.OverriddenProperty)
                        {
                            ignoredOverrides.Add(overridden);
                        }
                    }

                    if (member.IsStatic || IsIgnored(member) || ignoredOverrides.Contains(member))
                    {
                        continue;
                    }

                    // The generator enqueues public field types even when IncludeFields is false;
                    // it excludes JsonIgnore(Always) members before enqueuing their types. JsonInclude
                    // enqueues even inaccessible members; net10 reports SYSLIB1038 separately.
                    // https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Text.Json/gen/JsonSourceGenerator.Parser.cs
                    ITypeSymbol? memberType = member switch
                    {
                        IPropertySymbol property when !property.IsIndexer &&
                            (property.GetMethod?.DeclaredAccessibility == Accessibility.Public ||
                             property.SetMethod?.DeclaredAccessibility == Accessibility.Public || HasAttribute(property, include)) => property.Type,
                        IFieldSymbol field when !field.IsImplicitlyDeclared &&
                            (field.DeclaredAccessibility == Accessibility.Public || HasAttribute(field, include)) => field.Type,
                        _ => null,
                    };
                    if (memberType is not null && analysis.Compilation.IsSymbolAccessibleWithin(memberType, context))
                    {
                        Visit(memberType, depth + 1);
                    }
                }
            }

            // STJ also generates metadata for the selected deserialization constructor parameters.
            var constructors = named.InstanceConstructors;
            var selected = constructors.FirstOrDefault(c => HasAttribute(c, constructor));
            var publicConstructors = constructors.Where(c => c.DeclaredAccessibility == Accessibility.Public).ToArray();
            selected ??= publicConstructors.FirstOrDefault(c => c.Parameters.IsEmpty)
                ?? (publicConstructors.Length == 1 ? publicConstructors[0] : null);
            if (selected is not null)
            {
                foreach (var parameter in selected.Parameters)
                {
                    Visit(parameter.Type, depth + 1);
                }
            }
        }

        private bool IsIgnored(ISymbol member)
        {
            var attribute = member.GetAttributes().FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, ignore));
            return attribute is not null && (!attribute.NamedArguments.Any(a => a.Key == "Condition")
                || attribute.NamedArguments.Any(a => a.Key == "Condition" && a.Value.Value is int condition && condition == 1));
        }

        private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol? attribute) => attribute is not null
            && symbol.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute));
    }
}
