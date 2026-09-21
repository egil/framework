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
/// <param name="InvariantContext">
/// The factory in the validators class that <c>IsValueValid</c> gets its <c>ValidationContext</c>
/// from, so an attribute that overrides only <c>IsValid(object, ValidationContext)</c> gets a
/// context instead of the null the single-argument <c>IsValid</c> would forward.
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
    InvariantContextInfo InvariantContext,
    ImmutableArray<ValidationAttributeInfo> Attributes,
    ImmutableArray<ValidationAttributeInfo> AsyncAttributes,
    ImmutableArray<ValidationAttributeInfo> ContextAttributes)
{
    public static ValidationAttributeModel Empty { get; } = new(PreferredValidatorsTypeName, new InvariantContextInfo(PreferredInvariantContextFactoryName, string.Empty, SuppressTrimWarning: false), ImmutableArray<ValidationAttributeInfo>.Empty, ImmutableArray<ValidationAttributeInfo>.Empty, ImmutableArray<ValidationAttributeInfo>.Empty);

    public const string PreferredValidatorsTypeName = "ValueValidators";

    public const string PreferredInvariantContextFactoryName = "CreateInvariantContext";
}

/// <summary>
/// The <c>ValidationContext</c> that the invariant attributes are evaluated against. A fresh one
/// is created per <c>IsValueValid</c> call rather than shared: the context is mutable
/// (<c>Items</c>, <c>MemberName</c>, <c>DisplayName</c>) and a context-aware attribute may write to
/// it, so a shared instance would leak state between validations and race between threads. Its
/// <c>ObjectInstance</c> is a sentinel <c>new object()</c>: the invariant validates a bare value
/// before any instance of the strongly typed primitive exists, so there is no instance to give.
/// <c>MemberName</c> and <c>DisplayName</c> are the positional parameter's name, which is what the
/// attributes' error messages are formatted with.
/// </summary>
/// <param name="FactoryMethodName">
/// Name of the static method in the validators class that creates the context. The construction
/// goes through a method so that a trim warning suppression has a member to sit on without
/// having to be placed on <c>IsValueValid</c> itself.
/// </param>
/// <param name="CreationExpression">Source of the expression that constructs the context.</param>
/// <param name="SuppressTrimWarning">
/// Whether the factory method must carry an <c>UnconditionalSuppressMessage</c> for IL2026: true
/// when only the <c>RequiresUnreferencedCode</c> constructors of <c>ValidationContext</c> exist and
/// the compilation has the attribute (.NET 5 to 9), false when the trim safe constructor is used
/// (.NET 10 and later) or the attribute does not exist (netstandard2.0, which has no trim analysis).
/// </param>
internal sealed record InvariantContextInfo(string FactoryMethodName, string CreationExpression, bool SuppressTrimWarning);

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
