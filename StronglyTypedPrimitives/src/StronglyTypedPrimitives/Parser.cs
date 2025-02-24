using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StronglyTypedPrimitives;

internal static class Parser
{
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

    internal static IEnumerable<string> GetValidationAttributes(ParameterSyntax parameter, SemanticModel semanticModel)
    {
        var parameterSymbol = semanticModel.GetDeclaredSymbol(parameter);
        if (parameterSymbol is null)
            yield break;

        foreach (var attribute in parameterSymbol.GetAttributes())
        {
            if (!IsValidationAttribute(attribute))
            {
                continue;
            }

            var constructorArgs = GetAttributeConstructorArgs(attribute);
            var namedArgs = string.Join(", ", attribute.NamedArguments.Select(na => $"{na.Key} = {na.Value.ToCSharpString()}"));
            var args = string.Join(", ", new[] { constructorArgs, namedArgs }.Where(s => !string.IsNullOrEmpty(s)));
            yield return $"new global::{attribute.AttributeClass!.ToDisplayString()}({args})";
        }
    }

    internal static string GetAttributeConstructorArgs(AttributeData attribute)
    {
        var constructor = attribute.AttributeConstructor;
        if (constructor == null)
            return string.Empty;

        var args = new List<string>();
        var paramsParameter = constructor.Parameters.FirstOrDefault(p => p.IsParams);

        if (paramsParameter == null)
        {
            // No params parameter, handle normally
            return string.Join(", ", attribute.ConstructorArguments.Select(arg => arg.ToCSharpString()));
        }

        // Handle regular arguments before params array
        var paramsArrayStart = constructor.Parameters.IndexOf(paramsParameter);
        for (var i = 0; i < paramsArrayStart; i++)
        {
            args.Add(attribute.ConstructorArguments[i].ToCSharpString());
        }

        // Handle params array
        if (attribute.ConstructorArguments.Length > paramsArrayStart)
        {
            var paramsArg = attribute.ConstructorArguments[paramsArrayStart];
            if (paramsArg.Kind == TypedConstantKind.Array)
            {
                // If we have multiple values, create an array
                var values = paramsArg.Values.Select(v => v.ToCSharpString());
                if (values.Any())
                {
                    if (values.Count() > 1)
                    {
                        args.Add($"new [] {{{string.Join(", ", values)}}}");
                    }
                    else
                    {
                        args.Add(values.First());
                    }
                }
            }
            else
            {
                // Single argument passed to params
                args.Add(paramsArg.ToCSharpString());
            }
        }

        return string.Join(", ", args);
    }

    internal static bool IsValidationAttribute(AttributeData attribute)
    {
        var baseType = attribute.AttributeClass?.BaseType;
        while (baseType is not null)
        {
            if (baseType.ToDisplayString() == "System.ComponentModel.DataAnnotations.ValidationAttribute")
            {
                return true;
            }

            baseType = baseType.BaseType;
        }
        return false;
    }

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
}
