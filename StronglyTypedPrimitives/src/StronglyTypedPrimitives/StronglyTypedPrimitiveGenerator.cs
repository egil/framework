using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StronglyTypedPrimitives;

[Generator]
public sealed class StronglyTypedPrimitiveGenerator : IIncrementalGenerator
{
    private static string CodeHeader { get; } = """
        //------------------------------------------------------------------------------
        // <auto-generated>
        //     This code was generated by a tool.
        //
        //     Changes to this file may cause incorrect behavior and will be lost if
        //     the code is regenerated.
        // </auto-generated>
        //------------------------------------------------------------------------------
        #nullable enable
        """;

    private const string StronglyTypedPrimitivesNamespace = "StronglyTypedPrimitives";

    private static string IStronglyTypedPrimitive => $"{StronglyTypedPrimitivesNamespace}.IStronglyTypedPrimitive`1";

    private static string GeneratedCodeConstructor => $@"System.CodeDom.Compiler.GeneratedCodeAttribute(""{typeof(StronglyTypedPrimitiveGenerator).Assembly.FullName}"", ""{typeof(StronglyTypedPrimitiveGenerator).Assembly.GetName().Version}"")";

    private static string GeneratedCodeAttribute => $"[{GeneratedCodeConstructor}]";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var recordCandidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) =>
                    node is RecordDeclarationSyntax r &&
                    r.Modifiers.Any(SyntaxKind.PartialKeyword) &&
                    r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) &&
                    r.AttributeLists.Count > 0 &&
                    r.ParameterList?.Parameters.Count == 1,
                transform: static (context, _) =>
                {
                    var recordDecl = (RecordDeclarationSyntax)context.Node;
                    var model = context.SemanticModel;
                    return model.GetDeclaredSymbol(recordDecl) is INamedTypeSymbol symbol
                        && symbol.GetAttributes().Any(IsStronglyTypedPrimitiveAttribute)
                        && recordDecl.ParameterList?.Parameters.Count == 1
                        && recordDecl.ParameterList.Parameters[0] is { Type: not null } parameter
                        ? new StronglyTypedTypeInfo(recordDecl, Parser.GetNamespace(recordDecl), parameter.Type, parameter)
                        : null;
                }
            )
            .Where(static x => x is not null);

        var compilationAndRecords = context.CompilationProvider.Combine(recordCandidates.Collect());

        context.RegisterSourceOutput(compilationAndRecords, (spc, source) =>
        {
            var (compilation, stronglyTypedInfos) = source;
            foreach (var stronglyTypedInfo in stronglyTypedInfos)
            {
                if (stronglyTypedInfo is null)
                {
                    continue;
                }

                var generatedSource = GenerateStronglyTypedSource(stronglyTypedInfo, compilation);
                spc.AddSource($"{stronglyTypedInfo.Target.Identifier.Text}.g.cs", generatedSource);
            }
        });
    }

    private static bool IsStronglyTypedPrimitiveAttribute(AttributeData attribute)
        => attribute.AttributeClass?.Name == "StronglyTyped"
        || attribute.AttributeClass?.Name == "StronglyTypedAttribute";

    private static string GenerateStronglyTypedSource(StronglyTypedTypeInfo info, Compilation compilation)
    {
        var semanticModel = compilation.GetSemanticModel(info.Target.SyntaxTree);
        var targetTypeSymbol = semanticModel.GetDeclaredSymbol(info.Target) ?? throw new InvalidOperationException("Cannot get type symbol for target type.");
        var underlyingTypeSymbol = semanticModel.GetTypeInfo(info.UnderlyingType).Type ?? throw new InvalidOperationException("Cannot get type symbol for underlying type.");
        var targetTypeMembers = targetTypeSymbol.GetMembers().ToHashSet(SymbolEqualityComparer.Default);

        var genericInterfaces = new[]
        {
            semanticModel.Compilation.GetTypeByMetadataName("System.IParsable`1"),
            semanticModel.Compilation.GetTypeByMetadataName("System.ISpanParsable`1"),
            semanticModel.Compilation.GetTypeByMetadataName("System.IUtf8SpanParsable`1"),
            semanticModel.Compilation.GetTypeByMetadataName("System.IComparable`1"),
        };

        var interfaces = new[]
        {
            semanticModel.Compilation.GetTypeByMetadataName("System.IComparable"),
            semanticModel.Compilation.GetTypeByMetadataName("System.IFormattable"),
            semanticModel.Compilation.GetTypeByMetadataName("System.ISpanFormattable"),
            semanticModel.Compilation.GetTypeByMetadataName("System.IUtf8SpanFormattable"),
        };

        var supportedGenericInterfaces = genericInterfaces
            .OfType<INamedTypeSymbol>()
            .Where(genericInterface => semanticModel.IsTypeImplementingInterface(underlyingTypeSymbol, genericInterface.Construct(underlyingTypeSymbol)));
        var supportedInterfaces = interfaces
            .OfType<INamedTypeSymbol>()
            .Where(genericInterface => semanticModel.IsTypeImplementingInterface(underlyingTypeSymbol, genericInterface));

        var alwaysImplementInterfaces = new[]
        {
            semanticModel.Compilation
                .GetTypeByMetadataName(IStronglyTypedPrimitive)
                ?.Construct(underlyingTypeSymbol),
        };

        var interfacesToImplement = alwaysImplementInterfaces
            .OfType<INamedTypeSymbol>()
            .Concat(supportedGenericInterfaces.Select(@interface => @interface.Construct(targetTypeSymbol)))
            .Concat(supportedInterfaces)
            .ToArray();

        var unimplementedSymbols = interfacesToImplement
            .SelectMany(@interface => semanticModel.GetUnimplementedSymbols(targetTypeMembers, @interface))
            .ToList();

        string?[] typeParts = [
            CodeHeader,
            GetNamespaceDefinition(info),
            GeneratedCodeAttribute,
            GetPartialRecordStructDefinition(info, interfacesToImplement),
            "{",
            .. GetEmptyProperty(info, targetTypeMembers, underlyingTypeSymbol),
            .. GetValueProperty(info, targetTypeMembers, underlyingTypeSymbol),
            .. GetToStringMethod(info, targetTypeMembers, underlyingTypeSymbol),
            .. GetInterfaceSymbols(info, unimplementedSymbols, underlyingTypeSymbol),
            .. GetOperatorOverloads(info, targetTypeSymbol, targetTypeMembers),
            "}"
        ];

        return string.Join("\n", typeParts.OfType<string>());
    }

    internal static string? GetNamespaceDefinition(StronglyTypedTypeInfo info)
        => info.Namespace is not null
            ? $"\nnamespace {info.Namespace};\n"
            : null;

    internal static string GetPartialRecordStructDefinition(StronglyTypedTypeInfo info, IEnumerable<INamedTypeSymbol> interfaces)
        => $"""
        {info.Target.Modifiers} record struct {info.Target.Identifier} : {string.Join(", ", interfaces.Select(x => x.ToDisplayString()))}
        """;

    internal static IEnumerable<string> GetEmptyProperty(StronglyTypedTypeInfo info, IEnumerable<ISymbol> targetTypeMembers, ITypeSymbol underlyingTypeSymbol)
    {
        var hasEmptyProp = targetTypeMembers.OfType<IPropertySymbol>().Any(p => p.Name == "Empty" && p.IsStatic && p.Type.Equals(underlyingTypeSymbol, SymbolEqualityComparer.Default));
        if (hasEmptyProp)
        {
            yield break;
        }

        yield return $"""
                public static readonly {info.Target.Identifier} Empty = default;
            """;
    }

    internal static IEnumerable<string> GetValueProperty(StronglyTypedTypeInfo info, IEnumerable<ISymbol> targetTypeMembers, ITypeSymbol underlyingTypeSymbol)
    {
        const string throwIfValueIsInvalidName = "ThrowIfValueIsInvalid";
        var hasThrowIfValueIsInvalid = targetTypeMembers
            .OfType<IMethodSymbol>()
            .Any(m => m.Name == throwIfValueIsInvalidName
                   && m.IsStatic && m.Parameters.Length == 1
                   && m.Parameters[0].Type.Equals(underlyingTypeSymbol, SymbolEqualityComparer.Default)
                   && m.ReturnType.Equals(underlyingTypeSymbol, SymbolEqualityComparer.Default));

        if (!hasThrowIfValueIsInvalid)
        {
            yield return $$"""

                private static {{info.UnderlyingType}} {{throwIfValueIsInvalidName}}({{info.UnderlyingType}} value)
                {
                    IsValueValid(value, throwIfInvalid: true);
                    return value;
                }
            """;
        }

        // Check for explicit Value property declaration by end user. If they have created it
        // then the custom implementation is NOT created.
        var valueProp = targetTypeMembers.OfType<IPropertySymbol>().Single(x => x.Name == info.Parameter.Identifier.Text && x.Type.Equals(underlyingTypeSymbol, SymbolEqualityComparer.Default));
        var valueMethods = targetTypeMembers.OfType<IMethodSymbol>().Where(x => x.AssociatedSymbol?.Equals(valueProp, SymbolEqualityComparer.Default) == true);
        if (valueMethods.Any(x => !x.IsImplicitlyDeclared))
        {
            yield break;
        }

        var fieldName = $"@{info.Parameter.Identifier.ToString().ToLowerInvariant()}";
        var getMethodImplementation = underlyingTypeSymbol.SpecialType is SpecialType.System_String
            ? $"{fieldName} ?? string.Empty;"
            : $"{fieldName};";

        yield return $$"""

            private readonly {{info.UnderlyingType}} {{fieldName}} = {{throwIfValueIsInvalidName}}({{info.Parameter.Identifier}});       

            public {{info.UnderlyingType}} {{info.Parameter.Identifier}}
            {
                get => {{getMethodImplementation}}
                init
                {
                    {{fieldName}} = {{throwIfValueIsInvalidName}}(value);
                }
            }
        """;
    }

    internal static IEnumerable<string> GetToStringMethod(StronglyTypedTypeInfo info, IEnumerable<ISymbol> targetTypeMembers, ITypeSymbol underlyingTypeSymbol)
    {
        var toStringMethodExists = targetTypeMembers.OfType<IMethodSymbol>().Any(m => m.Name == "ToString" && !m.IsImplicitlyDeclared && m.ReturnType.SpecialType == SpecialType.System_String && m.Parameters.Length == 0);
        if (!toStringMethodExists)
        {
            yield return underlyingTypeSymbol.SpecialType is SpecialType.System_String
                ? $$"""

                    public override string ToString() => {{info.Parameter.Identifier}};
                """
                : $$"""

                    public override string ToString() => {{info.Parameter.Identifier}}.ToString();
                """;
        }
    }

    private static IEnumerable<string> GetOperatorOverloads(StronglyTypedTypeInfo info, INamedTypeSymbol targetTypeSymbol, IEnumerable<ISymbol> targetTypeMembers)
    {
        if (!targetTypeMembers.HasOperatorOverload(targetTypeSymbol, "op_Equality"))
        {
            yield return $"""

                public static bool operator ==({info.Target.Identifier} a, {info.Target.Identifier} b) => a.Equals(b);
            """;
        }
        if (!targetTypeMembers.HasOperatorOverload(targetTypeSymbol, "op_Inequality"))
        {
            yield return $"""

                public static bool operator !=({info.Target.Identifier} a, {info.Target.Identifier} b) => !(a == b);                
            """;
        }
        if (!targetTypeMembers.HasOperatorOverload(targetTypeSymbol, "op_GreaterThan"))
        {
            yield return $"""
                
                public static bool operator > ({info.Target.Identifier} a, {info.Target.Identifier} b) => a.CompareTo(b) > 0;                
            """;
        }
        if (!targetTypeMembers.HasOperatorOverload(targetTypeSymbol, "op_LessThan"))
        {
            yield return $"""

                public static bool operator < ({info.Target.Identifier} a, {info.Target.Identifier} b) => a.CompareTo(b) < 0;                
            """;
        }
        if (!targetTypeMembers.HasOperatorOverload(targetTypeSymbol, "op_GreaterThanOrEqual"))
        {
            yield return $"""

                public static bool operator >=({info.Target.Identifier} a, {info.Target.Identifier} b) => a.CompareTo(b) >= 0;
            """;
        }
        if (!targetTypeMembers.HasOperatorOverload(targetTypeSymbol, "op_LessThanOrEqual"))
        {
            yield return $"""
                
                public static bool operator <=({info.Target.Identifier} a, {info.Target.Identifier} b) => a.CompareTo(b) <= 0;
            """;
        }
    }

    private static IEnumerable<string> GetInterfaceSymbols(StronglyTypedTypeInfo info, IEnumerable<ISymbol> symbols, ITypeSymbol underlyingTypeSymbol)
    {
        foreach (var symbol in symbols)
        {
            if (symbol is not IMethodSymbol method)
            {
                yield return $"\n\t// Missing implementation for {symbol.OriginalDefinition.ToString()}";
                continue;
            }

            yield return method switch
            {
                { Name: "IsValueValid" } => GetIsValueValid(info, underlyingTypeSymbol),

                { Name: "Parse", Parameters: { Length: 2 } } when underlyingTypeSymbol.SpecialType is SpecialType.System_String => GetStringParse(info, method, underlyingTypeSymbol),
                { Name: "TryParse", Parameters: { Length: 3 } } when underlyingTypeSymbol.SpecialType is SpecialType.System_String => GetStringTryParse(info, method, underlyingTypeSymbol),

                { Name: "Parse", Parameters: { Length: 2 } } => GetParse(info, method, underlyingTypeSymbol),
                { Name: "TryParse", Parameters: { Length: 3 } } => GetTryParse(info, method, underlyingTypeSymbol),

                { Name: "CompareTo", ContainingType: { IsGenericType: true } } => GetCompareToOfT(info, method, underlyingTypeSymbol),
                { Name: "CompareTo", ContainingType: { IsGenericType: false } } => GetCompareTo(info, method, underlyingTypeSymbol),
                { Name: "ToString", Parameters: { Length: 2 } } => GetToString(info, method, underlyingTypeSymbol),
                { Name: "TryFormat", Parameters: { Length: 4 } } => GetTryFormat(info, method, underlyingTypeSymbol),

                _ => $"\n\t// Missing implementation for {symbol.OriginalDefinition.ToString()}",
            };
        }
    }

    private static string GetIsValueValid(StronglyTypedTypeInfo info, ITypeSymbol underlyingTypeSymbol)
        => $$"""

            public static bool IsValueValid({{underlyingTypeSymbol.ToDisplayString()}} value, bool throwIfInvalid)
                => true;
        """;

    private static string GetParse(StronglyTypedTypeInfo info, IMethodSymbol method, ITypeSymbol underlyingTypeSymbol)
        => $$"""

            public static {{info.Target.Identifier}} Parse({{method.Parameters[0]}}, {{method.Parameters[1]}})
            {
                var rawValue = {{underlyingTypeSymbol.ToDisplayString()}}.Parse({{method.Parameters[0].Name}}, {{method.Parameters[1].Name}});
                IsValueValid(rawValue, throwIfInvalid: true);
                return new {{info.Target.Identifier}}(rawValue);
            }
        """;

    private static string GetTryParse(StronglyTypedTypeInfo info, IMethodSymbol method, ITypeSymbol underlyingTypeSymbol)
        => $$"""

            public static bool TryParse({{method.Parameters[0]}}, {{method.Parameters[1]}}, [System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(returnValue: false)] {{method.Parameters[2]}})
            {
                if ({{underlyingTypeSymbol.ToDisplayString()}}.TryParse({{method.Parameters[0].Name}}, {{method.Parameters[1].Name}}, out var rawValue) && IsValueValid(rawValue, throwIfInvalid: false))
                {
                    {{method.Parameters[2].Name}} = new {{info.Target.Identifier}}(rawValue);
                    return true;
                }

                {{method.Parameters[2].Name}} = {{info.Target.Identifier}}.Empty;
                return false;
            }
        """;

    private static string GetStringParse(StronglyTypedTypeInfo info, IMethodSymbol method, ITypeSymbol underlyingTypeSymbol)
    {
        var rawValueString = method.Parameters[0].Type switch
        {
            { SpecialType: SpecialType.System_String } => $"{method.Parameters[0].Name}",
            _ => $"{method.Parameters[0].Name}.ToString()",
        };

        return $$"""

            public static {{info.Target.Identifier}} Parse({{method.Parameters[0]}}, {{method.Parameters[1]}})
            {
                var rawValue = {{rawValueString}};
                IsValueValid(rawValue, throwIfInvalid: true);
                return new {{info.Target.Identifier}}(rawValue);
            }
        """;
    }

    private static string GetStringTryParse(StronglyTypedTypeInfo info, IMethodSymbol method, ITypeSymbol underlyingTypeSymbol)
    {
        var rawValueString = method.Parameters[0].Type switch
        {
            { SpecialType: SpecialType.System_String } => $"{method.Parameters[0].Name}",
            _ => $"{method.Parameters[0].Name}.ToString()",
        };
        return $$"""

            public static bool TryParse({{method.Parameters[0]}}, {{method.Parameters[1]}}, [System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(returnValue: false)] {{method.Parameters[2]}})
            {
                if ({{rawValueString}} is {} rawValue && IsValueValid(rawValue, throwIfInvalid: false))
                {
                    {{method.Parameters[2].Name}} = new {{info.Target.Identifier}}(rawValue);
                    return true;
                }

                {{method.Parameters[2].Name}} = {{info.Target.Identifier}}.Empty;
                return false;
            }
        """;
    }

    private static string GetCompareToOfT(StronglyTypedTypeInfo info, IMethodSymbol method, ITypeSymbol underlyingTypeSymbol)
        => $$"""
        
        public int CompareTo({{method.Parameters[0]}})
            => {{info.Parameter.Identifier}}.CompareTo({{method.Parameters[0].Name}}.{{info.Parameter.Identifier}});
    """;

    private static string GetCompareTo(StronglyTypedTypeInfo info, IMethodSymbol method, ITypeSymbol underlyingTypeSymbol)
        => $$"""
        
        public int CompareTo({{method.Parameters[0]}})
        {
            if ({{method.Parameters[0].Name}} is null)
            {
                return 1;
            }

            if ({{method.Parameters[0].Name}} is {{info.Target.Identifier}} other)
            {
                return {{info.Parameter.Identifier}}.CompareTo(other.{{info.Parameter.Identifier}});
            }

            return (({{method.ContainingType.ToDisplayString()}}){{info.Parameter.Identifier}}).CompareTo({{method.Parameters[0].Name}});
        }
    """;

    private static string GetToString(StronglyTypedTypeInfo info, IMethodSymbol method, ITypeSymbol underlyingTypeSymbol)
        => $$"""
        
        public string ToString({{method.Parameters[0]}}, {{method.Parameters[1]}})
            => {{info.Parameter.Identifier}}.ToString({{method.Parameters[0].Name}}, {{method.Parameters[1].Name}});
    """;

    private static string GetTryFormat(StronglyTypedTypeInfo info, IMethodSymbol method, ITypeSymbol underlyingTypeSymbol)
        => $$"""
        
        public bool TryFormat({{method.Parameters[0]}}, {{method.Parameters[1]}}, {{method.Parameters[2]}}, {{method.Parameters[3]}})
            => (({{method.ContainingType.ToDisplayString()}}){{info.Parameter.Identifier}}).TryFormat({{method.Parameters[0].Name}}, out {{method.Parameters[1].Name}}, {{method.Parameters[2].Name}}, {{method.Parameters[3].Name}});
    """;
}
