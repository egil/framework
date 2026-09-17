using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Egil.StronglyTypedPrimitives;

internal static class Parser
{
    private const string ValidationAttributeTypeName = "System.ComponentModel.DataAnnotations.ValidationAttribute";

    // Only exists from .NET 11 on. Matching on the base type name means a compilation that cannot
    // see the type simply has no async attributes, without a separate lookup that has to be
    // tolerant of the type being absent.
    private const string AsyncValidationAttributeTypeName = "System.ComponentModel.DataAnnotations.AsyncValidationAttribute";

    internal static string? GetNamespace(RecordDeclarationSyntax structSymbol)
    {
        SyntaxNode? potentialNamespaceParent = structSymbol.Parent;
        while (potentialNamespaceParent is not null &&
               potentialNamespaceParent is not NamespaceDeclarationSyntax &&
               potentialNamespaceParent is not FileScopedNamespaceDeclarationSyntax)
        {
            potentialNamespaceParent = potentialNamespaceParent.Parent;
        }

        if (potentialNamespaceParent is BaseNamespaceDeclarationSyntax namespaceParent)
        {
            string @namespace = namespaceParent.Name.ToString();
            while (true)
            {
                if (namespaceParent.Parent is not NamespaceDeclarationSyntax namespaceParentParent)
                {
                    break;
                }

                namespaceParent = namespaceParentParent;
                @namespace = $"{namespaceParent.Name}.{@namespace}";
            }

            return @namespace;
        }

        return null;
    }

    internal static bool HasExistingIsValueValidMethod(StronglyTypedTypeInfo info, SemanticModel semanticModel)
    {
        var typeSymbol = semanticModel.GetDeclaredSymbol(info.Target)!;
        var underlyingTypeSymbol = semanticModel.GetTypeInfo(info.UnderlyingType).Type;
        return typeSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Any(m => m.Name == "IsValueValid"
                && m.IsStatic
                && m.Parameters.Length == 2
                && m.Parameters[0].Type.Equals(underlyingTypeSymbol, SymbolEqualityComparer.Default)
                && m.Parameters[1].Type.SpecialType == SpecialType.System_Boolean
                && m.Parameters[1].Name == "throwIfInvalid");
    }

    internal static (bool HasParse, bool HasTryParse) HasExistingIParsableImplementation(StronglyTypedTypeInfo info, SemanticModel semanticModel)
    {
        var typeSymbol = semanticModel.GetDeclaredSymbol(info.Target);
        bool hasParse = false;
        bool hasTryParse = false;

        if (typeSymbol is null)
        {
            return (hasParse, hasTryParse);
        }

        var iformatProviderTypeSymbol = semanticModel.Compilation.GetTypeByMetadataName("System.IFormatProvider");

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IMethodSymbol method && method.IsStatic && method.MethodKind == MethodKind.Ordinary)
            {
                if (method.Name == "Parse" &&
                    method.Parameters.Length == 2 &&
                    method.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                    method.Parameters[1].Type.Equals(iformatProviderTypeSymbol, SymbolEqualityComparer.Default))
                {
                    hasParse = true;
                }
                else if (method.Name == "TryParse" &&
                         method.Parameters.Length == 3 &&
                         method.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                         method.Parameters[1].Type.Equals(iformatProviderTypeSymbol, SymbolEqualityComparer.Default) &&
                         method.Parameters[2].RefKind == RefKind.Out &&
                         method.Parameters[2].Type.Equals(typeSymbol, SymbolEqualityComparer.Default))
                {
                    hasTryParse = true;
                }
            }
        }

        return (hasParse, hasTryParse);
    }

    internal static (bool HasToString, bool HasToStringWithFormat, bool HasToStringWithFormatProvider) HasExistingIFormattableImplementation(StronglyTypedTypeInfo info, SemanticModel semanticModel)
    {
        var iformatProviderTypeSymbol = semanticModel.Compilation.GetTypeByMetadataName("System.IFormatProvider");
        var typeSymbol = semanticModel.GetDeclaredSymbol(info.Target);
        bool hasToString = false;
        bool hasToStringWithFormat = false;
        bool hasToStringWithFormatProvider = false;

        if (typeSymbol is null)
        {
            return (hasToString, hasToStringWithFormat, hasToStringWithFormatProvider);
        }

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
            {
                if (method.Name == "ToString" && method.Parameters.Length == 0 && method.IsOverride && !method.IsImplicitlyDeclared)
                {
                    hasToString = true;
                }
                else if (method.Name == "ToString" && method.Parameters.Length == 1 && method.Parameters[0].Type.SpecialType == SpecialType.System_String)
                {
                    hasToStringWithFormat = true;
                }
                else if (method.Name == "ToString" && method.Parameters.Length == 2 && method.Parameters[0].Type.SpecialType == SpecialType.System_String && method.Parameters[1].Type.Equals(iformatProviderTypeSymbol, SymbolEqualityComparer.Default))
                {
                    hasToStringWithFormatProvider = true;
                }
            }
        }

        return (hasToString, hasToStringWithFormat, hasToStringWithFormatProvider);
    }

    // Only attributes applied to the parameter itself take part. That is the default target on a
    // positional record parameter and the parameter symbol is what carries them, so attributes the
    // user redirected with property: or field: never show up here.
    internal static ValidationAttributeModel GetValidationAttributes(ParameterSyntax parameter, SemanticModel semanticModel)
    {
        if (semanticModel.GetDeclaredSymbol(parameter) is not { } parameterSymbol)
        {
            return ValidationAttributeModel.Empty;
        }

        var attributes = ImmutableArray.CreateBuilder<ValidationAttributeInfo>();
        var asyncAttributes = ImmutableArray.CreateBuilder<ValidationAttributeInfo>();

        foreach (var attribute in parameterSymbol.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeClass || !DerivesFrom(attributeClass, ValidationAttributeTypeName))
            {
                continue;
            }

            var isAsync = DerivesFrom(attributeClass, AsyncValidationAttributeTypeName);
            var target = isAsync ? asyncAttributes : attributes;
            var index = target.Count;
            target.Add(new ValidationAttributeInfo(
                index,
                isAsync ? $"asyncValueValidator{index}" : $"valueValidator{index}",
                attributeClass.ToDisplayString(),
                string.Join(", ", attribute.ConstructorArguments.Select(FormatAttributeArgument)),
                FormatNamedArguments(attribute.NamedArguments),
                attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? parameter.Identifier.GetLocation()));
        }

        return new ValidationAttributeModel(attributes.ToImmutable(), asyncAttributes.ToImmutable());
    }

    // Attribute arguments are re-emitted as C# so the generated field constructs the attribute
    // exactly as the user declared it. ToCSharpString already quotes strings, qualifies enum
    // members and writes typeof(...), but it prints an array as a bare initializer and a number
    // without any type information, so Range(1.0, 2.0) would come back as Range(1, 2) and bind to
    // the int overload, and AllowedValues(1L) would box an int instead of a long.
    private static string FormatAttributeArgument(TypedConstant argument)
    {
        if (argument.IsNull)
        {
            return "null";
        }

        return argument.Kind switch
        {
            TypedConstantKind.Array => $"new {argument.Type!.ToDisplayString()} {{ {string.Join(", ", argument.Values.Select(FormatAttributeArgument))} }}",
            TypedConstantKind.Primitive when RequiresTypedLiteral(argument.Type!) => FormatTypedPrimitive(argument),
            _ => argument.ToCSharpString(),
        };
    }

    // int, bool, char and string literals already carry their type; every other numeric type is
    // written as a cast of the literal so the constant keeps the type it had on the attribute.
    private static bool RequiresTypedLiteral(ITypeSymbol type)
        => type.SpecialType is SpecialType.System_Single
            or SpecialType.System_Double
            or SpecialType.System_Int64
            or SpecialType.System_UInt64
            or SpecialType.System_UInt32
            or SpecialType.System_Int16
            or SpecialType.System_UInt16
            or SpecialType.System_Byte
            or SpecialType.System_SByte;

    private static string FormatTypedPrimitive(TypedConstant argument)
    {
        // ToCSharpString prints NaN and the infinities in a form that is not a C# literal.
        var literal = argument.Value switch
        {
            double d when double.IsNaN(d) => "double.NaN",
            double d when double.IsPositiveInfinity(d) => "double.PositiveInfinity",
            double d when double.IsNegativeInfinity(d) => "double.NegativeInfinity",
            float f when float.IsNaN(f) => "float.NaN",
            float f when float.IsPositiveInfinity(f) => "float.PositiveInfinity",
            float f when float.IsNegativeInfinity(f) => "float.NegativeInfinity",
            _ => argument.ToCSharpString(),
        };

        return $"({argument.Type!.ToDisplayString()}){literal}";
    }

    private static string FormatNamedArguments(ImmutableArray<KeyValuePair<string, TypedConstant>> namedArguments)
        => namedArguments.Length == 0
            ? string.Empty
            : $"{{ {string.Join(", ", namedArguments.Select(argument => $"{argument.Key} = {FormatAttributeArgument(argument.Value)}"))} }}";

    internal static bool IsUnderlyingTypeIParsableOrString(SemanticModel semanticModel, ITypeSymbol underlyingTypeSymbol)
    {
        var iParsableInterface = semanticModel.Compilation.GetTypeByMetadataName("System.IParsable`1");
        bool includeIParsable = false;

        if (underlyingTypeSymbol.SpecialType == SpecialType.System_String)
        {
            includeIParsable = true;
        }
        else if (iParsableInterface is not null)
        {
            includeIParsable = underlyingTypeSymbol.AllInterfaces.Any(i => i.OriginalDefinition.Equals(iParsableInterface, SymbolEqualityComparer.Default));
        }

        return includeIParsable;
    }

    internal static bool IsUnderlyingTypeString(StronglyTypedTypeInfo info, SemanticModel semanticModel)
    {
        var underlyingTypeSymbol = semanticModel.GetTypeInfo(info.UnderlyingType).Type;
        var isStringType = underlyingTypeSymbol?.SpecialType == SpecialType.System_String;
        return isStringType;
    }

    internal static IEnumerable<ISymbol> GetUnimplementedSymbols<TInterface>(INamedTypeSymbol target, SemanticModel semanticModel)
    {
        var interfaceType = semanticModel.Compilation.GetTypeByMetadataName(typeof(TInterface).FullName!)
            ?? throw new InvalidOperationException($"Type symbol not found for {typeof(TInterface).FullName}.");

        var targetMembers = target.GetMembers().ToHashSet(SymbolEqualityComparer.Default);

        foreach (var interfaceMember in interfaceType.GetMembers())
        {
            if (!targetMembers.Contains(interfaceMember, SymbolEqualityComparer.Default))
            {
                yield return interfaceMember;
            }
        }
    }

    internal static bool IsTypeImplementing<TInterface>(ITypeSymbol target, SemanticModel semanticModel)
    {
        var interfaceType = semanticModel.Compilation.GetTypeByMetadataName(typeof(TInterface).FullName!)
            ?? throw new InvalidOperationException($"Type symbol not found for {typeof(TInterface).FullName}.");

        return target.AllInterfaces.Contains(interfaceType, SymbolEqualityComparer.Default);
    }


    // The attribute is only emitted when both System.Text.Json and the shared converter from the
    // Abstractions assembly are visible to the compilation. The netstandard2.0 asset of the
    // Abstractions assembly has no converter, so such consumers are told instead of silently
    // losing JSON support.
    internal static JsonConverterSupport GetJsonConverterSupport(Compilation compilation, INamedTypeSymbol targetTypeSymbol)
    {
        var jsonConverterAttributeType = compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonConverterAttribute");
        if (jsonConverterAttributeType is null)
        {
            return JsonConverterSupport.NotApplicable;
        }

        var hasJsonConverterAttribute = targetTypeSymbol.GetAttributes().Any(a => a.AttributeClass?.Equals(jsonConverterAttributeType, SymbolEqualityComparer.Default) == true);
        if (hasJsonConverterAttribute)
        {
            return JsonConverterSupport.NotApplicable;
        }

        var sharedJsonConverterType = compilation.GetTypeByMetadataName("Egil.StronglyTypedPrimitives.StronglyTypedJsonConverter`2");
        return sharedJsonConverterType is not null
            ? JsonConverterSupport.GenerateAttribute
            : JsonConverterSupport.SharedConverterUnavailable;
    }

    internal static bool DerivesFromJsonSerializerContext(INamedTypeSymbol type)
        => DerivesFrom(type, "System.Text.Json.Serialization.JsonSerializerContext");

    private static bool DerivesFrom(INamedTypeSymbol type, string baseTypeName)
    {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.ToDisplayString() == baseTypeName)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// What the generator can do about System.Text.Json for a strongly typed primitive.
/// </summary>
internal enum JsonConverterSupport
{
    /// <summary>System.Text.Json is not referenced, or the user declared a JsonConverter attribute themselves.</summary>
    NotApplicable,

    /// <summary>The shared converter is available; emit the JsonConverter attribute.</summary>
    GenerateAttribute,

    /// <summary>System.Text.Json is referenced but the shared converter is not (netstandard2.0 asset); report STP004.</summary>
    SharedConverterUnavailable,
}
