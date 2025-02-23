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
}
