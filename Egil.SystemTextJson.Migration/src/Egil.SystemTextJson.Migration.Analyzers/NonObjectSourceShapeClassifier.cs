using Microsoft.CodeAnalysis;

namespace Egil.SystemTextJson.Migration.Analyzers;

internal sealed class NonObjectSourceShapeClassifier
{
    private readonly ShapeSymbols shapes;

    public NonObjectSourceShapeClassifier(Compilation compilation, HashSet<INamedTypeSymbol> selectorAttributes, bool hasBuilderPropertyOverride) =>
        shapes = ShapeSymbols.Create(compilation, selectorAttributes, hasBuilderPropertyOverride);

    public ClassifiedShape? Classify(ITypeSymbol source, INamedTypeSymbol marker) => shapes.Classify(source, marker);
}

internal enum SourceShape
{
    Unknown,
    String,
    Number,
    Boolean,
    Collection,
    Dictionary,
}

internal sealed class ClassifiedShape
{
    public ClassifiedShape(SourceShape shape, bool isLegacy, string? elementDiscriminator)
    {
        Shape = shape;
        IsLegacy = isLegacy;
        ElementDiscriminator = elementDiscriminator;
    }

    public SourceShape Shape { get; }

    public bool IsLegacy { get; }

    public string? ElementDiscriminator { get; }

    public bool CanCollideWith(ClassifiedShape other) => Shape == other.Shape && IsLegacy == other.IsLegacy
        && (string.IsNullOrEmpty(ElementDiscriminator) || string.IsNullOrEmpty(other.ElementDiscriminator)
            || ElementDiscriminator == other.ElementDiscriminator);
}

internal sealed class ShapeSymbols
{
    private readonly HashSet<ITypeSymbol> stringTypes;
    private readonly HashSet<ITypeSymbol> numberTypes;
    private readonly INamedTypeSymbol? enumerable;
    private readonly INamedTypeSymbol? asyncEnumerable;
    private readonly INamedTypeSymbol? dictionary;
    private readonly INamedTypeSymbol? readOnlyDictionary;
    private readonly INamedTypeSymbol? nonGenericDictionary;
    private readonly INamedTypeSymbol? memory;
    private readonly INamedTypeSymbol? readOnlyMemory;
    private readonly INamedTypeSymbol? jsonConverter;
    private readonly ITypeSymbol objectType;
    private readonly HashSet<INamedTypeSymbol> selectorAttributes;
    private readonly bool hasBuilderPropertyOverride;

    private ShapeSymbols(Compilation compilation, HashSet<INamedTypeSymbol> selectorAttributes, bool hasBuilderPropertyOverride)
    {
        this.selectorAttributes = selectorAttributes;
        this.hasBuilderPropertyOverride = hasBuilderPropertyOverride;
        stringTypes = CreateSet(compilation, "System.DateTime", "System.DateTimeOffset", "System.DateOnly", "System.TimeOnly", "System.TimeSpan", "System.Guid", "System.Uri", "System.Version");
        numberTypes = CreateSet(compilation, "System.Half", "System.Int128", "System.UInt128", "System.Numerics.BFloat16", "System.Numerics.Decimal32", "System.Numerics.Decimal64", "System.Numerics.Decimal128");
        enumerable = compilation.GetTypeByMetadataName("System.Collections.IEnumerable");
        asyncEnumerable = compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerable`1");
        dictionary = compilation.GetTypeByMetadataName("System.Collections.Generic.IDictionary`2");
        readOnlyDictionary = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyDictionary`2");
        nonGenericDictionary = compilation.GetTypeByMetadataName("System.Collections.IDictionary");
        memory = compilation.GetTypeByMetadataName("System.Memory`1");
        readOnlyMemory = compilation.GetTypeByMetadataName("System.ReadOnlyMemory`1");
        jsonConverter = compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonConverterAttribute");
        objectType = compilation.GetSpecialType(SpecialType.System_Object);
    }

    public static ShapeSymbols Create(Compilation compilation, HashSet<INamedTypeSymbol> selectorAttributes, bool hasBuilderPropertyOverride) =>
        new(compilation, selectorAttributes, hasBuilderPropertyOverride);

    public ClassifiedShape? Classify(ITypeSymbol source, INamedTypeSymbol marker)
    {
        source = UnwrapNullable(source);
        if (HasConverterOverride(source))
        {
            return null;
        }
        if (source.SpecialType is SpecialType.System_String or SpecialType.System_Char || stringTypes.Contains(source) || IsByteMemory(source))
        {
            return new ClassifiedShape(SourceShape.String, IsLegacy(source), null);
        }

        if (source.SpecialType is SpecialType.System_Boolean)
        {
            return new ClassifiedShape(SourceShape.Boolean, IsLegacy(source), null);
        }

        if (source.TypeKind is TypeKind.Enum || source.SpecialType is SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal || numberTypes.Contains(source))
        {
            return new ClassifiedShape(SourceShape.Number, IsLegacy(source), null);
        }

        if (source is IArrayTypeSymbol array)
        {
            return array.ElementType.SpecialType is SpecialType.System_Byte
                ? new ClassifiedShape(SourceShape.String, IsLegacy(source), null)
                : new ClassifiedShape(SourceShape.Collection, false, GetElementDiscriminatorKey(array.ElementType, marker));
        }

        if (source is not INamedTypeSymbol named)
        {
            return null;
        }

        if (IsDictionary(named))
        {
            return new ClassifiedShape(SourceShape.Dictionary, false, GetElementDiscriminatorKey(GetDictionaryValueType(named), marker));
        }

        if (IsByteMemory(named))
        {
            return new ClassifiedShape(SourceShape.String, IsLegacy(source), null);
        }

        if (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, memory)
            || SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, readOnlyMemory))
        {
            return new ClassifiedShape(SourceShape.Collection, false, GetElementDiscriminatorKey(named.TypeArguments[0], marker));
        }

        var elementType = GetEnumerableElementType(named);
        return elementType is null
            ? null
            : new ClassifiedShape(SourceShape.Collection, false, GetElementDiscriminatorKey(elementType, marker));
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type) => type is INamedTypeSymbol named
        && named.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T
        ? named.TypeArguments[0]
        : type;

    private bool IsByteMemory(ITypeSymbol type) => type is INamedTypeSymbol named
        && named.TypeArguments.Length == 1
        && named.TypeArguments[0].SpecialType is SpecialType.System_Byte
        && (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, memory)
            || SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, readOnlyMemory));

    private bool IsDictionary(INamedTypeSymbol type) => IsDictionaryContract(type) || type.AllInterfaces.Any(IsDictionaryContract);

    private bool IsDictionaryContract(INamedTypeSymbol type)
        => SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, dictionary)
            || SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, readOnlyDictionary)
            || SymbolEqualityComparer.Default.Equals(type, nonGenericDictionary);

    private ITypeSymbol GetDictionaryValueType(INamedTypeSymbol type)
    {
        var contract = IsDictionaryContract(type) ? type : type.AllInterfaces.First(IsDictionaryContract);
        return contract.TypeArguments.Length == 2 ? contract.TypeArguments[1] : type;
    }

    private ITypeSymbol? GetEnumerableElementType(INamedTypeSymbol type)
    {
        if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, asyncEnumerable)
            || type.OriginalDefinition.SpecialType is SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            return type.TypeArguments[0];
        }

        var contract = type.AllInterfaces.FirstOrDefault(@interface =>
            SymbolEqualityComparer.Default.Equals(@interface.OriginalDefinition, asyncEnumerable)
            || @interface.OriginalDefinition.SpecialType is SpecialType.System_Collections_Generic_IEnumerable_T);
        return contract?.TypeArguments[0] ?? (SymbolEqualityComparer.Default.Equals(type, enumerable)
            || type.AllInterfaces.Any(@interface => SymbolEqualityComparer.Default.Equals(@interface, enumerable))
            ? objectType
            : null);
    }

    private static bool IsLegacy(ITypeSymbol type)
    {
        type = UnwrapNullable(type);
        return type.SpecialType is SpecialType.System_String or SpecialType.System_Boolean
            || type.TypeKind is TypeKind.Enum
            || type.SpecialType is SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
    }

    private bool HasConverterOverride(ITypeSymbol source)
        => source.GetAttributes().Any(attribute => IsOrDerivesFrom(attribute.AttributeClass, jsonConverter));

    private static bool IsOrDerivesFrom(INamedTypeSymbol? candidate, INamedTypeSymbol? baseType)
    {
        for (var current = candidate; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private string GetElementDiscriminatorKey(ITypeSymbol elementType, INamedTypeSymbol marker)
    {
        elementType = UnwrapNullable(elementType);
        if (elementType is IArrayTypeSymbol or ITypeParameterSymbol)
        {
            return "";
        }

        if (elementType is not INamedTypeSymbol named || HasConverterOverride(named))
        {
            return "";
        }

        // Builder callbacks can replace a declared discriminator, and a configured
        // default property can collide with a source's explicit property name.
        if (named.GetAttributes().Any(attribute => selectorAttributes.Any(selector => IsOrDerivesFrom(attribute.AttributeClass, selector))))
        {
            return "";
        }

        AttributeData? inheritedAttribute = null;
        for (INamedTypeSymbol? current = named; current is not null; current = current.BaseType)
        {
            var attribute = current.GetAttributes().FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, marker));
            if (attribute is null)
            {
                continue;
            }

            inheritedAttribute = attribute;
            break;
        }

        if (inheritedAttribute is null)
        {
            return "";
        }

        var configuredProperty = inheritedAttribute.NamedArguments.FirstOrDefault(argument => argument.Key == "TypeDiscriminatorPropertyName").Value.Value as string;
        if (configuredProperty is null && hasBuilderPropertyOverride)
        {
            return "";
        }

        var propertyName = configuredProperty ?? "$type";
        var declaredAttribute = named.GetAttributes().FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, marker));
        var discriminator = declaredAttribute?.NamedArguments.FirstOrDefault(argument => argument.Key == "TypeDiscriminator").Value.Value as string ?? GetRuntimeFullName(named);
        return propertyName + ":" + discriminator;
    }

    private static string GetRuntimeFullName(INamedTypeSymbol type)
    {
        var names = new Stack<string>();
        for (var current = type; current is not null; current = current.ContainingType)
        {
            names.Push(current.Name + (current.IsGenericType
                ? "[" + string.Join(",", current.TypeArguments.Select(argument => argument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) + "]"
                : ""));
        }

        var prefix = type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString() + ".";
        return prefix + string.Join("+", names);
    }

    private static HashSet<ITypeSymbol> CreateSet(Compilation compilation, params string[] metadataNames)
    {
        var result = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var metadataName in metadataNames)
        {
            if (compilation.GetTypeByMetadataName(metadataName) is { } type)
            {
                result.Add(type);
            }
        }

        return result;
    }
}
