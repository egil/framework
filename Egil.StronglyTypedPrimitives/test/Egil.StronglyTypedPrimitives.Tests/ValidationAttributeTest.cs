using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Egil.StronglyTypedPrimitives;

public abstract class ValidationAttributeTestBase
{
    // A stand-in for the .NET 11 base type, declared under its real namespace so the generator
    // resolves it by name exactly as it would against the .NET 11 reference assemblies.
    public const string FakeAsyncValidationAttribute = """
        namespace System.ComponentModel.DataAnnotations
        {
            public abstract class AsyncValidationAttribute : ValidationAttribute
            {
            }
        }
        """;

    protected static async Task VerifyGeneratedSource(string input)
    {
        await SnapshotTestHelper.Verify<StronglyTypedPrimitiveGenerator>(
            input,
            LanguageVersion.LatestMajor,
            out var compilation
        );

        Assert.Empty(
            compilation
                .GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity > DiagnosticSeverity.Warning)
        );
    }
}

public class Validation_attributes_with_positional_and_named_arguments : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([EmailAddress, StringLength(254, MinimumLength = 3)] string Value);
            """);
}

public class Validation_attributes_with_params_argument : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([AllowedValues("a", "b")] string Value);
            """);
}

public class Validation_attributes_with_typeof_argument : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([CustomValidation(typeof(FooRules), nameof(FooRules.Validate))] string Value);

            public static class FooRules
            {
                public static ValidationResult? Validate(string value)
                    => value.StartsWith("x") ? ValidationResult.Success : new ValidationResult("Must start with x");
            }
            """);
}

public class Validation_attributes_with_enum_argument : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([DataType(DataType.EmailAddress)] string Value);
            """);
}

public class Validation_attributes_with_floating_point_arguments : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([Range(0.5, 1)] double Value);
            """);
}

public class Validation_attributes_with_array_and_typed_numeric_arguments : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([AllowedValues(1L, 2L), Limits(new[] { 1, 2 }, Weight = 1.5f)] long Value);

            public sealed class LimitsAttribute(int[] steps) : ValidationAttribute
            {
                public int[] Steps { get; } = steps;

                public float Weight { get; set; }

                public override bool IsValid(object? value) => true;
            }
            """);
}

public class Validation_attributes_on_alternative_parameter_name : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([Range(1, 10)] int Data);
            """);
}

public class Validation_attributes_with_user_written_IsValueValid : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([EmailAddress] string Value)
            {
                public static bool IsValueValid(string value, bool throwIfInvalid)
                    => true;
            }
            """);
}

public class Validation_attributes_when_a_user_member_takes_the_validators_type_name : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([EmailAddress] string Value)
            {
                private static readonly int valueValidator0 = 1;

                public static int ValueValidators => valueValidator0;
            }
            """);
}

public class Validation_attributes_targeting_the_property_are_ignored : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([property: Required] string Value);
            """);
}

public class Async_validation_attributes_are_excluded_from_IsValueValid : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource($$"""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([Required, RemoteCheck("https://example.com")] string Value);

                public sealed class RemoteCheckAttribute(string url) : AsyncValidationAttribute
                {
                    public string Url { get; } = url;
                }
            }

            {{FakeAsyncValidationAttribute}}
            """);
}
