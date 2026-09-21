using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Egil.StronglyTypedPrimitives;

/// <summary>
/// The validation attributes declared on the positional parameter of a strongly typed primitive,
/// collected once per type so every generated member that validates the wrapped value works from
/// the same list instead of re-reading the attribute data.
/// </summary>
/// <param name="ValidatorsTypeName">
/// Name of the nested static class that holds the attribute instances. The fields live there
/// rather than on the target type itself because a user's static initializer on the target type
/// (<c>public static readonly Foo Default = new(1);</c>) runs through <c>IsValueValid</c>, and the
/// order of static field initializers across partial declarations is not defined, so a field on
/// the target type could still be null at that point. A nested class is initialized on its own
/// first use, independent of the target type's initializer order.
/// </param>
/// <param name="Attributes">
/// Attributes deriving from <c>ValidationAttribute</c> that are part of the value invariant,
/// in declaration order.
/// </param>
/// <param name="AsyncAttributes">
/// Attributes deriving from <c>AsyncValidationAttribute</c> (.NET 11 and later). They cannot run
/// inside the synchronous <c>IsValueValid</c>, so they are kept separate for the members that can
/// await them.
/// </param>
/// <param name="ContextAttributes">
/// Attributes that override <c>RequiresValidationContext</c>, <c>CustomValidationAttribute</c>
/// among them. <c>IsValueValid</c> has no <c>ValidationContext</c> to give them, so they are kept
/// separate for the members that do (<c>IValidatableObject.Validate</c>).
/// </param>
internal sealed record ValidationAttributeModel(
    string ValidatorsTypeName,
    ImmutableArray<ValidationAttributeInfo> Attributes,
    ImmutableArray<ValidationAttributeInfo> AsyncAttributes,
    ImmutableArray<ValidationAttributeInfo> ContextAttributes)
{
    public static ValidationAttributeModel Empty { get; } = new(PreferredValidatorsTypeName, ImmutableArray<ValidationAttributeInfo>.Empty, ImmutableArray<ValidationAttributeInfo>.Empty, ImmutableArray<ValidationAttributeInfo>.Empty);

    public const string PreferredValidatorsTypeName = "ValueValidators";
}

/// <summary>
/// One validation attribute on the positional parameter, reduced to the source text needed to
/// re-create it as a static field in the generated validators class.
/// </summary>
/// <param name="Index">Zero-based position within its list on <see cref="ValidationAttributeModel"/>.</param>
/// <param name="FieldName">
/// Name of the generated static field holding the attribute instance. Generated code reads it as
/// <c>{ValidatorsTypeName}.{FieldName}</c>; the field is a member of the nested class, so it cannot
/// clash with anything the user declared on the target type.
/// </param>
/// <param name="AttributeTypeName">Attribute type rooted with <c>global::</c>, as written in generated code.</param>
/// <param name="AttributeDisplayName">Fully qualified attribute type without the <c>global::</c> root, for diagnostics.</param>
/// <param name="ConstructorArguments">Comma separated constructor argument source, or empty.</param>
/// <param name="NamedArguments">Object initializer source for the named arguments (<c>{ A = 1 }</c>), or empty.</param>
/// <param name="Location">Where the attribute is applied in user code, for diagnostics.</param>
internal sealed record ValidationAttributeInfo(
    int Index,
    string FieldName,
    string AttributeTypeName,
    string AttributeDisplayName,
    string ConstructorArguments,
    string NamedArguments,
    Location Location)
{
    public string CreationExpression
        => NamedArguments.Length == 0
            ? $"new {AttributeTypeName}({ConstructorArguments})"
            : $"new {AttributeTypeName}({ConstructorArguments}) {NamedArguments}";
}
