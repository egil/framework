using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Egil.StronglyTypedPrimitives;

internal record class StronglyTypedTypeInfo(
    RecordDeclarationSyntax Target,
    string? Namespace,
    TypeSyntax UnderlyingType,
    ParameterSyntax Parameter)
{
    public string FullyQualifiedTypeName { get; }
        = Namespace is not null
            ? $"{Namespace}.{Target.Identifier}"
            : Target.Identifier.ToString();
}
