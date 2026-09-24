using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Egil.SystemTextJson.Migration.Analyzers;

/// <summary>Reports source-generated union contexts that need the migration union classifier.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnionClassifierAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = DiagnosticDescriptorFactory.Create(
        "STJM0008", "Migratable union requires a classifier",
        "Union '{0}' includes a JsonMigratable case; add [JsonUnion(TypeClassifier = typeof(JsonMigratableUnionTypeClassifier))] to coordinate migration routing with SYSLIB1227",
        DiagnosticSeverity.Warning);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var marker = start.Compilation.GetTypeByMetadataName("Egil.SystemTextJson.Migration.JsonMigratableAttribute");
            var serializable = start.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonSerializableAttribute");
            var union = start.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonUnionAttribute");
            var classifier = start.Compilation.GetTypeByMetadataName("Egil.SystemTextJson.Migration.JsonMigratableUnionTypeClassifier");
            if (marker is null || serializable is null || union is null || classifier is null)
            {
                return;
            }

            start.RegisterSymbolAction(symbolContext => AnalyzeContext(symbolContext, marker, serializable, union, classifier), SymbolKind.NamedType);
        });
    }

    private static void AnalyzeContext(SymbolAnalysisContext context, INamedTypeSymbol marker, INamedTypeSymbol serializable, INamedTypeSymbol union, INamedTypeSymbol classifier)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!IsSerializerContext(type, context.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonSerializerContext")))
        {
            return;
        }
        foreach (var attribute in type.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, serializable)
                || attribute.ConstructorArguments.Length == 0
                || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol unionType
                || !IsUnion(unionType, context.CancellationToken)
                || HasClassifier(unionType, union, classifier)
                || !HasMigratableCase(unionType, marker, context.Compilation, context.CancellationToken))
            {
                continue;
            }

            var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? type.Locations[0];
            context.ReportDiagnostic(Diagnostic.Create(Rule, location, unionType.Name));
        }
    }

    private static bool IsUnion(INamedTypeSymbol type, CancellationToken cancellationToken) => type.GetAttributes().Any(attribute =>
        attribute.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.UnionAttribute")
        || type.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(cancellationToken).ChildTokens().Any(token => token.Text == "union" && !token.IsKind(SyntaxKind.IdentifierToken)));

    private static bool IsSerializerContext(INamedTypeSymbol type, INamedTypeSymbol? serializerContext)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current, serializerContext)) return true;
        return false;
    }

    private static bool HasClassifier(INamedTypeSymbol type, INamedTypeSymbol union, INamedTypeSymbol classifier) => type.GetAttributes().Any(attribute =>
        SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, union)
        && attribute.NamedArguments.Any(argument => argument.Key == "TypeClassifier"
            && argument.Value.Value is ITypeSymbol configuredClassifier
            && SymbolEqualityComparer.Default.Equals(configuredClassifier, classifier)));

    private static bool HasMigratableCase(INamedTypeSymbol type, INamedTypeSymbol marker, Compilation compilation, CancellationToken cancellationToken) =>
        HasMigratableCase(type, marker, compilation, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), cancellationToken);

    private static bool HasMigratableCase(INamedTypeSymbol type, INamedTypeSymbol marker, Compilation compilation, HashSet<INamedTypeSymbol> visited, CancellationToken cancellationToken)
    {
        if (!visited.Add(type))
        {
            return false;
        }

        if (type.DeclaringSyntaxReferences.IsEmpty)
        {
            return type.InstanceConstructors.Any(constructor =>
                constructor.Parameters.Length == 1
                && constructor.Parameters[0].Type is INamedTypeSymbol caseType
                && CaseRequiresClassifier(caseType, marker, compilation, visited, cancellationToken));
        }

        return type.DeclaringSyntaxReferences.Any(reference =>
        {
            var syntax = reference.GetSyntax(cancellationToken);
#pragma warning disable RS1030 // Union constituent types are exposed only by the SDK syntax in Roslyn 4.8.
            var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
#pragma warning restore RS1030
            return syntax.ChildNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ParameterListSyntax>().SelectMany(static parameters => parameters.Parameters).Any(parameter =>
                parameter.Type is not null
                && semanticModel.GetTypeInfo(parameter.Type, cancellationToken).Type is INamedTypeSymbol caseType
                && CaseRequiresClassifier(caseType, marker, compilation, visited, cancellationToken));
        });
    }

    private static bool CaseRequiresClassifier(INamedTypeSymbol caseType, INamedTypeSymbol marker, Compilation compilation, HashSet<INamedTypeSymbol> visited, CancellationToken cancellationToken)
    {
        if (caseType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && caseType.TypeArguments[0] is INamedTypeSymbol underlying)
        {
            caseType = underlying;
        }

        return HasMarker(caseType, marker)
            || IsUnion(caseType, cancellationToken)
                && HasMigratableCase(caseType, marker, compilation, visited, cancellationToken);
    }

    private static bool HasMarker(INamedTypeSymbol type, INamedTypeSymbol marker)
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
}
