using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Egil.StronglyTypedPrimitives;

internal static class Parser
{
    private const string ValidationAttributeTypeName = "System.ComponentModel.DataAnnotations.ValidationAttribute";

    private const string ValidationContextTypeName = "System.ComponentModel.DataAnnotations.ValidationContext";

    private const string UnconditionalSuppressMessageAttributeTypeName = "System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute";

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

    // Only attributes applied to the parameter itself take part. That is the default target on a
    // positional record parameter and the parameter symbol is what carries them, so attributes the
    // user redirected with property: or field: never show up here.
    internal static ValidationAttributeModel GetValidationAttributes(ParameterSyntax parameter, SemanticModel semanticModel, IEnumerable<ISymbol> targetTypeMembers)
    {
        if (semanticModel.GetDeclaredSymbol(parameter) is not { } parameterSymbol)
        {
            return ValidationAttributeModel.Empty;
        }

        // The nested validators class shares the target type with whatever the user declared in
        // their partial declaration, so its name is only used when no existing member has it. The
        // target type's own name is reserved too: a nested type cannot share it (CS0542).
        var reservedNames = new HashSet<string>(targetTypeMembers.Select(member => member.Name), StringComparer.Ordinal)
        {
            parameterSymbol.ContainingType.Name,
        };
        var validatorsTypeName = GetUnusedMemberName(ValidationAttributeModel.PreferredValidatorsTypeName, reservedNames);
        var attributes = ImmutableArray.CreateBuilder<ValidationAttributeInfo>();
        var asyncAttributes = ImmutableArray.CreateBuilder<ValidationAttributeInfo>();
        var contextAttributes = ImmutableArray.CreateBuilder<ValidationAttributeInfo>();

        foreach (var attribute in parameterSymbol.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeClass || !DerivesFrom(attributeClass, ValidationAttributeTypeName))
            {
                continue;
            }

            var (target, fieldPrefix) = DerivesFrom(attributeClass, AsyncValidationAttributeTypeName) ? (asyncAttributes, "asyncValueValidator")
                : RequiresValidationContext(attributeClass) ? (contextAttributes, "contextValueValidator")
                : (attributes, "valueValidator");
            var index = target.Count;
            target.Add(new ValidationAttributeInfo(
                index,
                $"{fieldPrefix}{index}",
                GlobalName(attributeClass),
                attributeClass.ToDisplayString(),
                string.Join(", ", attribute.ConstructorArguments.Select(FormatAttributeArgument)),
                FormatNamedArguments(attribute.NamedArguments),
                attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? parameter.Identifier.GetLocation()));
        }

        // The context field shares the validators class with the attribute fields, so its name
        // steps aside for any of those (none is called that today, but the field prefixes are the
        // only thing keeping it so).
        var holderMemberNames = new HashSet<string>(attributes.Concat(asyncAttributes).Concat(contextAttributes).Select(attribute => attribute.FieldName), StringComparer.Ordinal);
        var invariantContext = GetInvariantContext(semanticModel.Compilation, parameterSymbol.Name, holderMemberNames);

        return new ValidationAttributeModel(validatorsTypeName, invariantContext, attributes.ToImmutable(), asyncAttributes.ToImmutable(), contextAttributes.ToImmutable());
    }

    // ValidationContext(object) is marked RequiresUnreferencedCode because the DisplayName getter
    // falls back to reflection over the instance's type when no display name was set. .NET 10
    // added ValidationContext(object, string displayName, IServiceProvider?, IDictionary?) which is
    // trim safe for exactly that reason, so it is preferred whenever the compilation has it. On
    // earlier targets DisplayName is set up front, which keeps the getter off the reflection path
    // even though the constructor call itself still carries the annotation; the IL2026 that call
    // raises is therefore suppressed, where the compilation has the attribute to do so.
    private static InvariantContextInfo GetInvariantContext(Compilation compilation, string parameterName, HashSet<string> holderMemberNames)
    {
        var hasTrimSafeConstructor = compilation
            .GetTypeByMetadataName(ValidationContextTypeName)?
            .Constructors
            .Any(constructor => constructor.Parameters.Length == 4 && constructor.Parameters[1].Type.SpecialType == SpecialType.System_String) == true;
        var hasSuppressionAttribute = compilation.GetTypeByMetadataName(UnconditionalSuppressMessageAttributeTypeName) is not null;
        var name = SymbolDisplay.FormatLiteral(parameterName, quote: true);

        return new InvariantContextInfo(
            GetUnusedMemberName(ValidationAttributeModel.PreferredInvariantContextFieldName, holderMemberNames),
            GetUnusedMemberName(ValidationAttributeModel.PreferredInvariantContextFactoryName, holderMemberNames),
            hasTrimSafeConstructor
                ? $"new global::{ValidationContextTypeName}(new object(), {name}, null, null) {{ MemberName = {name} }}"
                : $"new global::{ValidationContextTypeName}(new object()) {{ MemberName = {name}, DisplayName = {name} }}",
            SuppressTrimWarning: !hasTrimSafeConstructor && hasSuppressionAttribute);
    }

    // RequiresValidationContext is virtual on ValidationAttribute and false there; the override can
    // sit on any class between the attribute and that base, so the whole chain below it is
    // searched. What the override returns is not evaluated: an attribute that bothers to override
    // it is taken at its word.
    private static bool RequiresValidationContext(INamedTypeSymbol attributeClass)
    {
        for (var type = attributeClass; type is not null && type.ToDisplayString() != ValidationAttributeTypeName; type = type.BaseType)
        {
            if (type.GetMembers("RequiresValidationContext").OfType<IPropertySymbol>().Any(property => property.IsOverride))
            {
                return true;
            }
        }

        return false;
    }

    // Appending underscores keeps the name recognisable and deterministic.
    private static string GetUnusedMemberName(string preferredName, HashSet<string> reservedNames)
    {
        var name = preferredName;
        while (!reservedNames.Add(name))
        {
            name += "_";
        }

        return name;
    }

    // Every type name written into generated code is rooted with global::. The generated file
    // sits in the target type's namespace, so an unrooted System.ComponentModel... or Rules...
    // would bind to a nested SomeNamespace.System or SomeNamespace.Rules when the consumer has one.
    private static string GlobalName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    // Attribute arguments are re-emitted as C# so the generated field constructs the attribute
    // exactly as the user declared it. ToCSharpString already quotes strings and characters, but
    // it prints an array as a bare initializer and a number without any type information, so
    // Range(1.0, 2.0) would come back as Range(1, 2) and bind to the int overload, and
    // AllowedValues(1L) would box an int instead of a long. Enum members and typeof arguments are
    // written here too, because ToCSharpString does not root their type names with global::.
    private static string FormatAttributeArgument(TypedConstant argument)
    {
        // A bare null is ambiguous between overloads such as Check(string?) and Check(Type?)
        // (CS0121), so the constant keeps the type of the parameter it was bound to. Attribute
        // parameters that accept null are all reference types, hence the `?`; the annotation is
        // stripped first so an already annotated type does not end up as `string??`.
        if (argument.IsNull)
        {
            return argument.Type is { } type
                ? $"({GlobalName(type.WithNullableAnnotation(NullableAnnotation.None))}?)null"
                : "null";
        }

        return argument.Kind switch
        {
            TypedConstantKind.Array => $"new {GlobalName(argument.Type!)} {{ {string.Join(", ", argument.Values.Select(FormatAttributeArgument))} }}",
            TypedConstantKind.Primitive when RequiresTypedLiteral(argument.Type!) => FormatTypedPrimitive(argument),
            TypedConstantKind.Enum => FormatEnum(argument),
            TypedConstantKind.Type => $"typeof({GlobalName((ITypeSymbol)argument.Value!)})",
            _ => argument.ToCSharpString(),
        };
    }

    // A value that matches a single member is written by name; anything else (a flags combination
    // or a value the enum does not declare) is a cast of the underlying constant, which is valid
    // C# for every enum value even if less readable.
    private static string FormatEnum(TypedConstant argument)
    {
        var enumType = argument.Type!;
        var member = enumType.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(field => field.HasConstantValue && Equals(field.ConstantValue, argument.Value));

        return member is not null
            ? $"{GlobalName(enumType)}.{member.Name}"
            : $"({GlobalName(enumType)})({Convert.ToString(argument.Value, System.Globalization.CultureInfo.InvariantCulture)})";
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

        return $"({GlobalName(argument.Type!)}){literal}";
    }

    private static string FormatNamedArguments(ImmutableArray<KeyValuePair<string, TypedConstant>> namedArguments)
        => namedArguments.Length == 0
            ? string.Empty
            : $"{{ {string.Join(", ", namedArguments.Select(argument => $"{argument.Key} = {FormatAttributeArgument(argument.Value)}"))} }}";

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
