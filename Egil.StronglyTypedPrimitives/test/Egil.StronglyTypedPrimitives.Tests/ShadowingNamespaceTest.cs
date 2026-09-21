namespace Egil.StronglyTypedPrimitives;

// A consumer that declares a namespace called System below the one the primitive lives in
// (SomeNamespace.System here) shadows the global System inside the generated
// `namespace SomeNamespace;`, so every System.* name the generator writes, from the interface list
// to the ValidateAsync signature, only resolves when rooted with global::. The user's own code is
// unaffected because it reaches those types through using directives; for the same reason the
// context attribute is declared here rather than taken from FakeContextValidationAttribute,
// which spells System.ComponentModel.DataAnnotations out inside SomeNamespace. Both
// IValidatableObject and IAsyncValidatableObject are declared with a sync and an async attribute,
// so the validators holder with its context factory, Validate and ValidateAsync, which name the
// most System types, are all part of the compile check.
public class Generated_type_names_survive_a_shadowing_System_namespace_for_int : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource($$"""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([Range(1, 10), RemoteCheck] int Value) : IValidatableObject, IAsyncValidatableObject;

                public sealed class RemoteCheckAttribute : AsyncValidationAttribute
                {
                }
            }

            namespace SomeNamespace.System
            {
                public static class Shadow { }
            }

            {{FakeAsyncValidationApi}}
            """);
}

// The string primitive takes the other code paths: the string overloads of Parse and TryParse,
// the string CompareTo and, through the parameter named Data, the explicit
// IStronglyTypedPrimitive<TSelf, TPrimitive>.Value implementation. A context attribute adds the
// context-attribute evaluation to Validate, and the members named Validate and ValidateAsync
// make both generated methods explicit interface implementations, with ValidateAsync reaching
// Validate through the interface cast.
public class Generated_type_names_survive_a_shadowing_System_namespace_for_string : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource($$"""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([StringLength(10, MinimumLength = 2), TenantScoped, RemoteCheck] string Data) : IValidatableObject, IAsyncValidatableObject
                {
                    public bool Validate() => IsValueValid(Data, throwIfInvalid: false);

                    public bool ValidateAsync() => Validate();
                }

                public sealed class RemoteCheckAttribute : AsyncValidationAttribute
                {
                }

                public sealed class TenantScopedAttribute : ValidationAttribute
                {
                    public override bool RequiresValidationContext => true;

                    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
                        => validationContext.Items.ContainsKey("tenant") ? ValidationResult.Success : new ValidationResult("No tenant");
                }
            }

            namespace SomeNamespace.System
            {
                public static class Shadow { }
            }

            {{FakeAsyncValidationApi}}
            """);
}
