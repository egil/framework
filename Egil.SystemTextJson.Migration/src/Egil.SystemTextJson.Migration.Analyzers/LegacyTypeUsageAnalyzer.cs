using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Egil.SystemTextJson.Migration.Analyzers;

/// <summary>Restricts marked historical payloads to their migration boundary.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LegacyTypeUsageAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = DiagnosticDescriptorFactory.Create(
        "STJM0010", "Legacy payload type used outside migration",
        "Legacy payload type '{0}' should only be used in its declaration, direct migration implementation, or JSON migration setup",
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
            var marker = start.Compilation.GetTypeByMetadataName("Egil.SystemTextJson.Migration.JsonMigrationLegacyTypeAttribute");
            if (marker is null || !MigrationSymbols.TryCreate(start.Compilation, out var symbols))
            {
                return;
            }

            start.RegisterSyntaxNodeAction(node => Analyze(node, marker, symbols!),
                SyntaxKind.IdentifierName, SyntaxKind.GenericName, SyntaxKind.InvocationExpression,
                SyntaxKind.ImplicitObjectCreationExpression, SyntaxKind.ElementAccessExpression, SyntaxKind.SimpleMemberAccessExpression, SyntaxKind.MemberBindingExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol marker, MigrationSymbols symbols)
    {
        var node = (ExpressionSyntax)context.Node;
        var symbol = context.SemanticModel.GetSymbolInfo(node, context.CancellationToken).Symbol;
        var type = symbol as ITypeSymbol ?? context.SemanticModel.GetTypeInfo(node, context.CancellationToken).Type;
        if (type is null || symbol is IDiscardSymbol || symbol is IMethodSymbol && node is SimpleNameSyntax)
        {
            return;
        }

        var legacy = symbol is ITypeSymbol && node is not IdentifierNameSyntax { IsVar: true }
            ? (type is INamedTypeSymbol named && IsMarked(named, marker) ? named : null)
            : FindLegacy(type, marker);
        if (legacy is null || IsDuplicateReference(context, node, legacy, marker) || IsAllowed(context, node, legacy, symbols))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation(), legacy.ToDisplayString()));
    }

    private static bool IsDuplicateReference(SyntaxNodeAnalysisContext context, ExpressionSyntax node, INamedTypeSymbol legacy, INamedTypeSymbol marker)
    {
        if (node.Parent is MemberAccessExpressionSyntax access && access.Name == node
            || node.Parent is MemberBindingExpressionSyntax)
        {
            return true;
        }

        // The declaration owns the inferred initializer type: `var old = ReadOld()`
        // gets one warning on var, but a different legacy type in the initializer still warns.
        var declaration = node.Ancestors().OfType<VariableDeclarationSyntax>().FirstOrDefault();
        if (declaration is not null && declaration.Type != node
            && (declaration.Type.IsVar || node is ImplicitObjectCreationExpressionSyntax)
            && SymbolEqualityComparer.Default.Equals(legacy,
                FindLegacy(context.SemanticModel.GetTypeInfo(declaration.Type, context.CancellationToken).Type, marker)))
        {
            return true;
        }

        // Member access has both a receiver and a result. A marked receiver already
        // owns the warning, while a current receiver returning a legacy value needs one.
        var receiver = node switch
        {
            MemberAccessExpressionSyntax member => member.Expression,
            ElementAccessExpressionSyntax element => element.Expression,
            InvocationExpressionSyntax invocation when invocation.Expression is MemberAccessExpressionSyntax member => member.Expression,
            _ => null,
        };
        return receiver is not null && SymbolEqualityComparer.Default.Equals(legacy,
            FindLegacy(context.SemanticModel.GetTypeInfo(receiver, context.CancellationToken).Type, marker));
    }

    private static bool IsMarked(INamedTypeSymbol type, INamedTypeSymbol marker) =>
        type.GetAttributes().Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker));

    private static INamedTypeSymbol? FindLegacy(ITypeSymbol? type, INamedTypeSymbol marker)
    {
        if (type is IArrayTypeSymbol array)
        {
            return FindLegacy(array.ElementType, marker);
        }

        if (type is not INamedTypeSymbol named)
        {
            return null;
        }

        if (IsMarked(named, marker))
        {
            return named;
        }

        return named.TypeArguments.Select(argument => FindLegacy(argument, marker)).FirstOrDefault(result => result is not null);
    }

    private static bool IsAllowed(SyntaxNodeAnalysisContext context, SyntaxNode node, INamedTypeSymbol legacy, MigrationSymbols symbols)
    {
        var enclosing = context.SemanticModel.GetEnclosingSymbol(node.SpanStart, context.CancellationToken);
        for (var owner = enclosing; owner is not null; owner = owner.ContainingSymbol)
        {
            if (owner is INamedTypeSymbol ownerType && SymbolEqualityComparer.Default.Equals(ownerType.OriginalDefinition, legacy.OriginalDefinition))
            {
                return true;
            }
        }

        var methodSyntax = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var method = methodSyntax is null ? enclosing as IMethodSymbol : context.SemanticModel.GetDeclaredSymbol(methodSyntax, context.CancellationToken);
        if (node.Ancestors().Any(ancestor => ancestor is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax))
        {
            method = null;
        }
        if (method is not null && method.MethodKind is not MethodKind.LocalFunction and not MethodKind.AnonymousFunction)
        {
            foreach (var contract in method.ContainingType.AllInterfaces.Where(contract => IsMigrationContract(contract, symbols)))
            {
                if (!contract.TypeArguments.Any(argument => SymbolEqualityComparer.Default.Equals(argument, legacy)))
                {
                    continue;
                }

                if (contract.GetMembers().Any(member => SymbolEqualityComparer.Default.Equals(
                    method.ContainingType.FindImplementationForInterfaceMember(member), method)))
                {
                    return true;
                }
            }
        }

        var baseType = node.Ancestors().OfType<BaseTypeSyntax>().FirstOrDefault();
        if (baseType is not null && context.SemanticModel.GetTypeInfo(baseType.Type, context.CancellationToken).Type is INamedTypeSymbol baseSymbol
            && IsMigrationContract(baseSymbol, symbols))
        {
            return true;
        }

        var attributeSyntax = node.Ancestors().OfType<AttributeSyntax>().FirstOrDefault();
        if (attributeSyntax is not null && node.Ancestors().OfType<TypeOfExpressionSyntax>().Any())
        {
            var attribute = (context.SemanticModel.GetSymbolInfo(attributeSyntax, context.CancellationToken).Symbol as IMethodSymbol)?.ContainingType;
            if (SymbolEqualityComparer.Default.Equals(attribute, context.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonSerializableAttribute")))
            {
                return true;
            }

            var argument = node.Ancestors().OfType<AttributeArgumentSyntax>().FirstOrDefault();
            if (SymbolEqualityComparer.Default.Equals(attribute, symbols.JsonMigratableAttribute)
                && argument?.NameEquals?.Name.Identifier.ValueText == "UndiscriminatedSourceType")
            {
                return true;
            }
        }

        var typeArguments = node.Ancestors().OfType<TypeArgumentListSyntax>().FirstOrDefault();
        if (typeArguments?.Parent is GenericNameSyntax generic
            && typeArguments.Arguments.Count == 3
            && typeArguments.Arguments[0].Span.Contains(node.Span)
            && (context.SemanticModel.GetSymbolInfo(generic, context.CancellationToken).Symbol as IMethodSymbol) is { Name: "RegisterMigrator" } registration
            && SymbolEqualityComparer.Default.Equals(registration.ContainingType,
                context.Compilation.GetTypeByMetadataName("Egil.SystemTextJson.Migration.JsonMigrationBuilder")))
        {
            return true;
        }

        return false;
    }

    private static bool IsMigrationContract(INamedTypeSymbol type, MigrationSymbols symbols) =>
        SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, symbols.Migrate)
        || SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, symbols.MigrateFrom);
}
