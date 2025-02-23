using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StronglyTypedPrimitives;

[Generator]
public sealed class StronglyTypedPrimitiveGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("StronglyTypedAttribute.g.cs", Emitter.GetStronglyTypedPrimitiveAttributeSource());
            ctx.AddSource("IStronglyTypedPrimitive.g.cs", Emitter.GetIStronglyTypedPrimitiveSource());
        });

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

                var generatedSource = GenerateTypeSource(stronglyTypedInfo, compilation);
                spc.AddSource($"{stronglyTypedInfo.Target.Identifier.Text}.g.cs", generatedSource);
            }
        });
    }

    private static string GenerateTypeSource(StronglyTypedTypeInfo info, Compilation compilation)
    {
        var semanticModel = compilation.GetSemanticModel(info.Target.SyntaxTree);

        return string.Join(
            "\n",
            new[]
            {
                Emitter.CodeHeader,
                Emitter.GetNamespaceDefinition(info),
                Emitter.GeneratedCodeAttribute,
                Emitter.GetTargetRecordStructDefinition(info),
                Emitter.GetNoneStaticField(info),
                Emitter.GetValuePropertyDefinition(info),
                Emitter.GetIsValueValidMethodDefinition(info, semanticModel),
                "}"
            }.OfType<string>());
    }

    private static bool IsStronglyTypedPrimitiveAttribute(AttributeData attribute)
        => attribute.AttributeClass?.Name == "StronglyTyped"
        || attribute.AttributeClass?.Name == "StronglyTypedAttribute";
}
