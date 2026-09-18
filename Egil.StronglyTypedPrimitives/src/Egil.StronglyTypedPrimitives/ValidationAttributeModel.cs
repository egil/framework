using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Egil.StronglyTypedPrimitives;

/// <summary>
/// The validation attributes declared on the positional parameter of a strongly typed primitive,
/// collected once per type so every generated member that validates the wrapped value works from
/// the same list instead of re-reading the attribute data.
/// </summary>
/// <param name="Attributes">
/// Attributes deriving from <c>ValidationAttribute</c> that are part of the value invariant,
/// in declaration order.
/// </param>
/// <param name="AsyncAttributes">
/// Attributes deriving from <c>AsyncValidationAttribute</c> (.NET 11 and later). They cannot run
/// inside the synchronous <c>IsValueValid</c>, so they are kept separate for the members that can
/// await them.
/// </param>
internal sealed record ValidationAttributeModel(
    ImmutableArray<ValidationAttributeInfo> Attributes,
    ImmutableArray<ValidationAttributeInfo> AsyncAttributes)
{
    public static ValidationAttributeModel Empty { get; } = new(ImmutableArray<ValidationAttributeInfo>.Empty, ImmutableArray<ValidationAttributeInfo>.Empty);
}

/// <summary>
/// One validation attribute on the positional parameter, reduced to the source text needed to
/// re-create it as a static field in the generated type.
/// </summary>
/// <param name="Index">Zero-based position within its list on <see cref="ValidationAttributeModel"/>.</param>
/// <param name="FieldName">Name of the generated static field holding the attribute instance.</param>
/// <param name="AttributeTypeName">Fully qualified attribute type, as written in generated code.</param>
/// <param name="ConstructorArguments">Comma separated constructor argument source, or empty.</param>
/// <param name="NamedArguments">Object initializer source for the named arguments (<c>{ A = 1 }</c>), or empty.</param>
/// <param name="Location">Where the attribute is applied in user code, for diagnostics.</param>
internal sealed record ValidationAttributeInfo(
    int Index,
    string FieldName,
    string AttributeTypeName,
    string ConstructorArguments,
    string NamedArguments,
    Location Location)
{
    public string CreationExpression
        => NamedArguments.Length == 0
            ? $"new {AttributeTypeName}({ConstructorArguments})"
            : $"new {AttributeTypeName}({ConstructorArguments}) {NamedArguments}";
}
