using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Egil.SystemTextJson.Migration.Analyzers;

/// <summary>Detects source-generated contexts that omit a migration source type.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContextSourceMetadataAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = DiagnosticDescriptorFactory.Create(
        "STJM0006",
        "JsonSerializerContext is missing migration source metadata",
        "JsonSerializerContext includes migratable target '{0}' but omits required migration source '{1}'",
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
            var marker = start.Compilation.GetTypeByMetadataName("Egil.SystemTextJson.Migration.JsonMigratableAttribute");
            var migrateFrom = start.Compilation.GetTypeByMetadataName("Egil.SystemTextJson.Migration.IMigrateFrom`2");
            var serializerContext = start.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonSerializerContext");
            var jsonSerializable = start.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonSerializableAttribute");
            var jsonInclude = start.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonIncludeAttribute");
            var jsonIgnore = start.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonIgnoreAttribute");
            if (marker is null || migrateFrom is null || serializerContext is null || jsonSerializable is null || jsonIgnore is null)
            {
                return;
            }

            start.RegisterSymbolAction(symbolContext => AnalyzeContext(symbolContext, marker, migrateFrom, serializerContext, jsonSerializable, jsonIgnore, jsonInclude), SymbolKind.NamedType);
        });
    }

    private static void AnalyzeContext(
        SymbolAnalysisContext context,
        INamedTypeSymbol marker,
        INamedTypeSymbol migrateFrom,
        INamedTypeSymbol serializerContext,
        INamedTypeSymbol jsonSerializable,
        INamedTypeSymbol jsonIgnore,
        INamedTypeSymbol? jsonInclude)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!IsSerializerContext(type, serializerContext))
        {
            return;
        }

        // JsonSerializable attributes seed generated metadata. The generator also follows
        // public object members and collection elements from each seed.
        var registeredTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var registrations = new List<(ITypeSymbol Type, Location Location)>();
        foreach (var attribute in type.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, jsonSerializable)
                || attribute.ConstructorArguments.Length != 1
                || attribute.ConstructorArguments[0].Value is not ITypeSymbol registeredType
                || !registeredTypes.Add(registeredType))
            {
                continue;
            }

            var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? type.Locations[0];
            registrations.Add((registeredType, location));
        }

        var reachableTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var targets = new Dictionary<INamedTypeSymbol, Location>(SymbolEqualityComparer.Default);
        var hasUnresolvedGraph = false;
        foreach (var (registeredType, location) in registrations)
        {
            foreach (var (reachable, canRequireMigration) in GetReachableTypes(registeredType, context, type, jsonIgnore, jsonInclude,
                () => hasUnresolvedGraph = true))
            {
                reachableTypes.Add(reachable);
                if (canRequireMigration && reachable is INamedTypeSymbol target && !HasUnknownContract(target, context.Compilation)
                    && HasMigrationMarker(target, marker) && !targets.ContainsKey(target))
                {
                    targets.Add(target, location);
                }
            }
        }

        if (hasUnresolvedGraph)
        {
            return;
        }

        foreach (var target in targets)
        {
            foreach (var source in GetMigrationSources(target.Key, migrateFrom))
            {
                if (!reachableTypes.Contains(source))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, target.Value, target.Key.Name, source.ToDisplayString()));
                }
            }
        }
    }

    private static bool IsSerializerContext(INamedTypeSymbol type, INamedTypeSymbol serializerContext)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, serializerContext))
            {
                return true;
            }
        }

        return false;
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

    private static IEnumerable<ITypeSymbol> GetMigrationSources(INamedTypeSymbol target, INamedTypeSymbol migrateFrom)
    {
        foreach (var contract in target.AllInterfaces)
        {
            // A base contract for a different target does not supply migration into this target.
            if (SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, migrateFrom)
                && SymbolEqualityComparer.Default.Equals(contract.TypeArguments[1], target))
            {
                yield return contract.TypeArguments[0];
            }
        }
    }

    private static IEnumerable<(ITypeSymbol Type, bool CanRequireMigration)> GetReachableTypes(
        ITypeSymbol root,
        SymbolAnalysisContext analysis,
        INamedTypeSymbol contextType,
        INamedTypeSymbol jsonIgnore,
        INamedTypeSymbol? jsonInclude,
        Action markUnresolved)
    {
        var pending = new Stack<(ITypeSymbol Type, int Depth, bool CanRequireMigration)>();
        var visitedNormal = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var visitedConverted = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var derivedType = analysis.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonDerivedTypeAttribute");
        var constructor = analysis.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonConstructorAttribute");
        var converter = analysis.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonConverterAttribute");
        pending.Push((root, 0, true));
        while (pending.Count > 0)
        {
            analysis.CancellationToken.ThrowIfCancellationRequested();
            var (current, depth, canRequireMigration) = pending.Pop();
            // Constructed recursive generics can expand forever without repeating a symbol.
            // Beyond this bound, source metadata cannot be proven absent from the graph.
            if (depth > 32 || current.TypeKind is TypeKind.Error or TypeKind.TypeParameter
                || current is INamedTypeSymbol unresolved && (unresolved.IsUnboundGenericType
                    || unresolved.TypeArguments.Any(argument => argument.TypeKind is TypeKind.Error or TypeKind.TypeParameter)))
            {
                markUnresolved();
                continue;
            }
            if (!(canRequireMigration ? visitedNormal : visitedConverted).Add(current))
            {
                continue;
            }

            yield return (current, canRequireMigration);
            // STJ still emits metadata for a member with its own converter, but that
            // member does not invoke migration for its type through this path.
            if (!canRequireMigration)
            {
                continue;
            }
            // A converter owns its JSON contract, so CLR members cannot establish generated
            // metadata or migration requirements beneath this node. Keep the node itself:
            // explicitly generated converter metadata still satisfies a source registration.
            if (HasUnknownContract(current, analysis.Compilation))
            {
                continue;
            }

            if (current is IArrayTypeSymbol array)
            {
                pending.Push((array.ElementType, depth + 1, true));
                continue;
            }

            if (current is not INamedTypeSymbol named || named.SpecialType != SpecialType.None || named.TypeKind == TypeKind.Enum)
            {
                continue;
            }

            if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                pending.Push((named.TypeArguments[0], depth + 1, true));
                continue;
            }

            foreach (var attribute in named.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, derivedType)
                    && attribute.ConstructorArguments.FirstOrDefault().Value is ITypeSymbol derived)
                {
                    pending.Push((derived, depth + 1, true));
                }
            }

            var enumerable = new[] { named }.Concat(named.AllInterfaces).FirstOrDefault(contract =>
                contract.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
                || SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition,
                    analysis.Compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerable`1")));
            if (enumerable is not null)
            {
                pending.Push((enumerable.TypeArguments[0], depth + 1, true));
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, analysis.Compilation.GetTypeByMetadataName("System.Memory`1"))
                || SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, analysis.Compilation.GetTypeByMetadataName("System.ReadOnlyMemory`1")))
            {
                pending.Push((named.TypeArguments[0], depth + 1, true));
                continue;
            }

            foreach (var member in GetSerializableMembers(named, jsonIgnore, jsonInclude, converter))
            {
                if (analysis.Compilation.IsSymbolAccessibleWithin(member.Type, contextType))
                {
                    pending.Push((member.Type, depth + 1, !member.ConverterOwned));
                }
            }

            var constructors = named.InstanceConstructors;
            var selected = constructors.FirstOrDefault(candidate => HasAttribute(candidate, constructor));
            var publicConstructors = constructors.Where(candidate => candidate.DeclaredAccessibility == Accessibility.Public).ToArray();
            selected ??= publicConstructors.FirstOrDefault(candidate => candidate.Parameters.IsEmpty)
                ?? (publicConstructors.Length == 1 ? publicConstructors[0] : null);
            if (selected is not null)
            {
                foreach (var parameter in selected.Parameters)
                {
                    pending.Push((parameter.Type, depth + 1, true));
                }
            }
        }
    }

    private static bool HasUnknownContract(ITypeSymbol type, Compilation compilation)
    {
        if (type.TypeKind is TypeKind.Error or TypeKind.TypeParameter)
        {
            return true;
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (named.IsUnboundGenericType || named.TypeArguments.Any(argument => argument.TypeKind is TypeKind.Error or TypeKind.TypeParameter))
        {
            return true;
        }

        var converterAttribute = compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonConverterAttribute");
        foreach (var attribute in named.GetAttributes())
        {
            for (var attributeType = attribute.AttributeClass; attributeType is not null; attributeType = attributeType.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(attributeType, converterAttribute))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<(ITypeSymbol Type, bool ConverterOwned)> GetSerializableMembers(INamedTypeSymbol type, INamedTypeSymbol jsonIgnore, INamedTypeSymbol? jsonInclude, INamedTypeSymbol? jsonConverter)
    {
        var hierarchy = new List<INamedTypeSymbol>();
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            hierarchy.Add(current);
        }

        if (type.TypeKind == TypeKind.Interface)
        {
            hierarchy.AddRange(type.AllInterfaces);
        }

        var ignoredOverrides = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var current in hierarchy)
        {
            foreach (var member in current.GetMembers())
            {
                var ignored = IsAlwaysIgnored(member, jsonIgnore);
                if (ignored && member is IPropertySymbol ignoredProperty)
                {
                    for (var overridden = ignoredProperty.OverriddenProperty; overridden is not null; overridden = overridden.OverriddenProperty)
                    {
                        ignoredOverrides.Add(overridden);
                    }
                }

                if (member.IsStatic || ignored || ignoredOverrides.Contains(member))
                {
                    continue;
                }

                var included = jsonInclude is not null && member.GetAttributes().Any(attribute =>
                    SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, jsonInclude));
                // STJ enqueues public field types regardless of IncludeFields, and included
                // non-public members also contribute metadata, even when accessibility is diagnosed.
                // https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Text.Json/gen/JsonSourceGenerator.Parser.cs
                if (member is IPropertySymbol property && !property.IsIndexer
                    && (included || property.GetMethod?.DeclaredAccessibility == Accessibility.Public
                        || property.SetMethod?.DeclaredAccessibility == Accessibility.Public))
                {
                    yield return (property.Type, HasConverterAttribute(property, jsonConverter));
                }
                else if (member is IFieldSymbol field && !field.IsImplicitlyDeclared
                    && (included || field.DeclaredAccessibility == Accessibility.Public))
                {
                    yield return (field.Type, HasConverterAttribute(field, jsonConverter));
                }
            }
        }
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol? attribute) => attribute is not null
        && symbol.GetAttributes().Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attribute));

    private static bool HasConverterAttribute(ISymbol symbol, INamedTypeSymbol? converter) => converter is not null
        && symbol.GetAttributes().Any(attribute =>
        {
            for (var current = attribute.AttributeClass; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, converter))
                {
                    return true;
                }
            }

            return false;
        });

    private static bool IsAlwaysIgnored(ISymbol member, INamedTypeSymbol jsonIgnore)
    {
        var attribute = member.GetAttributes().FirstOrDefault(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, jsonIgnore));
        if (attribute is null)
        {
            return false;
        }

        var condition = attribute.NamedArguments.FirstOrDefault(argument => argument.Key == "Condition");
        return condition.Key is null || condition.Value.Value is 1;
    }
}
