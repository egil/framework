using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Egil.StronglyTypedPrimitives;

internal static class Parser
{
    private const string ValidationAttributeTypeName = "System.ComponentModel.DataAnnotations.ValidationAttribute";

    private const string ValidationContextTypeName = "System.ComponentModel.DataAnnotations.ValidationContext";

    // Only exists from .NET 11 on. Matching on the base type name means a compilation that cannot
    // see the type simply has no async attributes, without a separate lookup that has to be
    // tolerant of the type being absent.
    private const string AsyncValidationAttributeTypeName = "System.ComponentModel.DataAnnotations.AsyncValidationAttribute";

    private const string ValidatableObjectTypeName = "System.ComponentModel.DataAnnotations.IValidatableObject";

    // Only exists from .NET 11 on. The generator runs once per target framework, so whether the
    // compilation can resolve the interface is the whole gate and the generated code needs no #if.
    private const string AsyncValidatableObjectTypeName = "System.ComponentModel.DataAnnotations.IAsyncValidatableObject";

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
        var validatorsTypeName = GetUnusedName(ValidationAttributeModel.PreferredValidatorsTypeName, reservedNames);
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

        // The context factory shares the validators class with the attribute fields, so its name
        // steps aside for any of those (none is called that today, but the field prefixes are the
        // only thing keeping it so).
        var holderMemberNames = new HashSet<string>(attributes.Concat(asyncAttributes).Concat(contextAttributes).Select(attribute => attribute.FieldName), StringComparer.Ordinal);
        var invariantContext = GetInvariantContext(semanticModel.Compilation, parameterSymbol.Name, holderMemberNames);

        return new ValidationAttributeModel(validatorsTypeName, invariantContext, attributes.ToImmutable(), asyncAttributes.ToImmutable(), contextAttributes.ToImmutable());
    }

    // ValidationContext(object) is marked RequiresUnreferencedCode because the DisplayName getter
    // falls back to reflection over the instance's type when no display name was set. .NET 10
    // added ValidationContext(object, string displayName, IServiceProvider?, IDictionary?) which is
    // trim safe for exactly that reason, and every supported .NET target has it. The
    // netstandard2.0 asset has not (System.ComponentModel.Annotations stops at the older
    // constructors), so there DisplayName is set up front instead, which keeps the getter off the
    // reflection path; that target has no trim analysis, so the annotation on the constructor
    // call goes unreported and needs no suppression.
    private static InvariantContextInfo GetInvariantContext(Compilation compilation, string parameterName, HashSet<string> holderMemberNames)
    {
        var hasTrimSafeConstructor = compilation
            .GetTypeByMetadataName(ValidationContextTypeName)?
            .Constructors
            .Any(constructor => constructor.Parameters.Length == 4 && constructor.Parameters[1].Type.SpecialType == SpecialType.System_String) == true;
        var name = SymbolDisplay.FormatLiteral(parameterName, quote: true);

        return new InvariantContextInfo(
            GetUnusedName(ValidationAttributeModel.PreferredInvariantContextFactoryName, holderMemberNames),
            hasTrimSafeConstructor
                ? $"new global::{ValidationContextTypeName}(new object(), {name}, null, null) {{ MemberName = {name} }}"
                : $"new global::{ValidationContextTypeName}(new object()) {{ MemberName = {name}, DisplayName = {name} }}");
    }

    // The built-in attribute that needs a context. Its RequiresValidationContext override exists
    // in the implementation assembly only: the reference assemblies of net10.0 and net11.0 leave
    // it out, so a build never sees the override and the attribute has to be known by name.
    private const string CustomValidationAttributeTypeName = "System.ComponentModel.DataAnnotations.CustomValidationAttribute";

    // RequiresValidationContext is virtual on ValidationAttribute and false there; the override can
    // sit on any class between the attribute and that base, so the whole chain below it is
    // searched. What the override returns is not evaluated: an attribute that bothers to override
    // it is taken at its word.
    private static bool RequiresValidationContext(INamedTypeSymbol attributeClass)
    {
        if (attributeClass.ToDisplayString() == CustomValidationAttributeTypeName)
        {
            return true;
        }

        for (var type = attributeClass; type is not null && type.ToDisplayString() != ValidationAttributeTypeName; type = type.BaseType)
        {
            if (type.GetMembers("RequiresValidationContext").OfType<IPropertySymbol>().Any(property => property.IsOverride))
            {
                return true;
            }
        }

        return false;
    }

    // Appending underscores keeps the name recognisable and deterministic; the chosen name is
    // reserved as well so a later name cannot land on it. Shared with the generated Validate,
    // whose parameter and locals must stay clear of the positional parameter and user members.
    internal static string GetUnusedName(string preferredName, HashSet<string> reservedNames)
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
    // Nullable reference annotations are kept: the generated file is #nullable enable, so a
    // `string?[]` written as `string[]` would make its null elements a CS8625 for the consumer.
    private static readonly SymbolDisplayFormat GlobalNameFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    internal static string GlobalName(ITypeSymbol type)
        => type.ToDisplayString(GlobalNameFormat);

    // Attribute arguments are re-emitted as C# so the generated field constructs the attribute
    // exactly as the user declared it. ToCSharpString already quotes strings and characters, but
    // it prints an array as a bare initializer and a number without any type information, so
    // Range(1.0, 2.0) would come back as Range(1, 2) and bind to the int overload, and
    // AllowedValues(1L) would box an int instead of a long. Enum members and typeof arguments are
    // written here too, because ToCSharpString does not root their type names with global::.
    private static string FormatAttributeArgument(TypedConstant argument)
    {
        // A bare null is ambiguous between overloads such as Check(string?) and Check(Type?)
        // (CS0121), so the constant keeps the type of the parameter it was bound to, annotated
        // as nullable because the generated file is #nullable enable.
        if (argument.IsNull)
        {
            return argument.Type is { } type
                ? $"({GlobalName(AsNullable(type))})null"
                : "null";
        }

        return argument.Kind switch
        {
            TypedConstantKind.Array => FormatArray(argument),
            TypedConstantKind.Primitive when RequiresTypedLiteral(argument.Type!) => FormatTypedPrimitive(argument),
            TypedConstantKind.Enum => FormatEnum(argument),
            TypedConstantKind.Type => $"typeof({GlobalName((ITypeSymbol)argument.Value!)})",
            _ => argument.ToCSharpString(),
        };
    }

    // The element type is spelled out on the array so the elements need no casts of their own: a
    // null element is written as a plain null, and makes the element type nullable, since the
    // constant's own type carries no annotation to tell `string[]` from `string?[]`.
    private static string FormatArray(TypedConstant argument)
    {
        var elementType = ((IArrayTypeSymbol)argument.Type!).ElementType;
        if (argument.Values.Any(element => element.IsNull))
        {
            elementType = AsNullable(elementType);
        }

        var elements = argument.Values.Select(element => element.IsNull ? "null" : FormatAttributeArgument(element));
        return $"new {GlobalName(elementType)}[] {{ {string.Join(", ", elements)} }}";
    }

    // Only reference types are annotated: attribute arguments cannot be Nullable<T>, so a null
    // constant or element always has a reference type, and annotating a value type would turn it
    // into Nullable<T>.
    private static ITypeSymbol AsNullable(ITypeSymbol type)
        => type.IsReferenceType ? type.WithNullableAnnotation(NullableAnnotation.Annotated) : type;

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
            ? $"{GlobalName(enumType)}.{EscapeIdentifier(member.Name)}"
            : $"({GlobalName(enumType)})({Convert.ToString(argument.Value, System.Globalization.CultureInfo.InvariantCulture)})";
    }

    // ISymbol.Name is the bare name, so a member declared as `@default` or `@class` comes back as
    // the keyword and has to be escaped again to be written as an identifier. Contextual keywords
    // (`var`, `async`, ...) are valid identifiers and need no escape.
    internal static string EscapeIdentifier(string name)
        => SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : $"@{name}";

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
            : $"{{ {string.Join(", ", namedArguments.Select(argument => $"{EscapeIdentifier(argument.Key)} = {FormatAttributeArgument(argument.Value)}"))} }}";

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

    // Generators cannot see each other's output, and the ASP.NET Core validation generator decides
    // whether a type is validatable by looking for IValidatableObject on the type symbol. Adding
    // the interface from here would therefore be invisible to it, so Validate is only filled in
    // when the user's own declaration names the interface, and a Validate the user wrote (implicit
    // or explicit) wins like every other generated member.
    internal static bool ShouldGenerateValidate(Compilation compilation, INamedTypeSymbol targetTypeSymbol)
        => ShouldGenerateInterfaceMethod(compilation, targetTypeSymbol, ValidatableObjectTypeName, "Validate");

    // IAsyncValidatableObject extends IValidatableObject, so a declaration naming it is also a
    // declaration of IValidatableObject and gets Validate under the same rules.
    internal static bool ShouldGenerateValidateAsync(Compilation compilation, INamedTypeSymbol targetTypeSymbol)
        => ShouldGenerateInterfaceMethod(compilation, targetTypeSymbol, AsyncValidatableObjectTypeName, "ValidateAsync");

    internal static bool DeclaresAsyncValidatableObject(Compilation compilation, INamedTypeSymbol targetTypeSymbol)
        => FindDeclaredInterface(compilation, targetTypeSymbol, AsyncValidatableObjectTypeName) is not null;

    // The generated ValidateAsync starts by forwarding to Validate. A Validate the user wrote as an
    // explicit interface implementation is only reachable through the interface, so the emitter
    // has to know to go through a cast instead of a direct call.
    internal static bool ImplementsValidateExplicitly(Compilation compilation, INamedTypeSymbol targetTypeSymbol)
        => FindDeclaredInterface(compilation, targetTypeSymbol, ValidatableObjectTypeName)?.GetMembers("Validate").OfType<IMethodSymbol>().FirstOrDefault() is { } validateMethod
            && targetTypeSymbol.FindImplementationForInterfaceMember(validateMethod) is IMethodSymbol { ExplicitInterfaceImplementations.Length: > 0 };

    private static bool ShouldGenerateInterfaceMethod(Compilation compilation, INamedTypeSymbol targetTypeSymbol, string interfaceTypeName, string methodName)
    {
        if (FindDeclaredInterface(compilation, targetTypeSymbol, interfaceTypeName) is not { } interfaceType)
        {
            return false;
        }

        var method = interfaceType.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault();
        return method is not null && targetTypeSymbol.FindImplementationForInterfaceMember(method) is null;
    }

    private static INamedTypeSymbol? FindDeclaredInterface(Compilation compilation, INamedTypeSymbol targetTypeSymbol, string interfaceTypeName)
    {
        var interfaceType = compilation.GetTypeByMetadataName(interfaceTypeName);
        return interfaceType is not null && targetTypeSymbol.AllInterfaces.Contains(interfaceType, SymbolEqualityComparer.Default)
            ? interfaceType
            : null;
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
