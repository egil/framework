namespace Egil.StronglyTypedPrimitives;

// Validate is only generated when the user's declaration names IValidatableObject, because the
// ASP.NET Core validation source generator cannot see interfaces added by another generator.
public class Validate_for_IValidatableObject_without_constraints : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(int Value) : IValidatableObject;
            """);
}

public class Validate_for_IValidatableObject_with_validation_attributes : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([EmailAddress, StringLength(254, MinimumLength = 3)] string Value) : IValidatableObject;
            """);
}

public class Validate_for_IValidatableObject_with_user_written_IsValueValid : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(int Value) : IValidatableObject
            {
                public static bool IsValueValid(int value, bool throwIfInvalid)
                {
                    if (value > 5)
                        return true;

                    if (throwIfInvalid)
                        throw new System.ArgumentException("Value must be larger than 5", nameof(value));

                    return false;
                }
            }
            """);
}

public class Validate_for_IValidatableObject_with_user_written_IsValueValid_on_alternative_parameter_name : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(int Data) : IValidatableObject
            {
                public static bool IsValueValid(int value, bool throwIfInvalid)
                    => value > 5;
            }
            """);
}

public class Validate_for_IValidatableObject_on_alternative_parameter_name : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([Range(1, 10)] int Data) : IValidatableObject;
            """);
}

public class Validate_for_IValidatableObject_with_parameter_named_like_a_result_local : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([Range(1, 10), AllowedValues(2, 4)] int result0) : IValidatableObject
            {
                public int results => 0;
            }
            """);
}

public class Validate_for_IValidatableObject_with_parameter_named_validationContext : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(int validationContext) : IValidatableObject
            {
                public static bool IsValueValid(int value, bool throwIfInvalid)
                {
                    if (value > 5)
                        return true;

                    if (throwIfInvalid)
                        throw new System.ArgumentException("Value must be larger than 5", nameof(value));

                    return false;
                }

                public int ex => 0;
            }
            """);
}

public class Validate_is_not_generated_when_the_user_writes_Validate : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using System.Collections.Generic;
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([Range(1, 10)] int Value) : IValidatableObject
            {
                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                    => System.Array.Empty<ValidationResult>();
            }
            """);
}

public class Validate_is_not_generated_when_the_user_writes_an_explicit_Validate : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using System.Collections.Generic;
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([Range(1, 10)] int Value) : IValidatableObject
            {
                IEnumerable<ValidationResult> IValidatableObject.Validate(ValidationContext validationContext)
                    => System.Array.Empty<ValidationResult>();
            }
            """);
}

public class Validate_is_not_generated_without_a_declared_IValidatableObject : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([Range(1, 10)] int Value);
            """);
}