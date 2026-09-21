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

    // An attribute that can only validate against a ValidationContext, with the
    // RequiresValidationContext override on a base class so the generator has to look past the
    // attribute's own declaration to find it.
    public const string FakeContextValidationAttribute = """
        namespace SomeNamespace
        {
            public abstract class ContextAwareValidationAttribute : System.ComponentModel.DataAnnotations.ValidationAttribute
            {
                public override bool RequiresValidationContext => true;
            }

            public sealed class TenantScopedAttribute : ContextAwareValidationAttribute
            {
                protected override System.ComponentModel.DataAnnotations.ValidationResult? IsValid(object? value, System.ComponentModel.DataAnnotations.ValidationContext validationContext)
                    => validationContext.Items.ContainsKey("tenant")
                        ? System.ComponentModel.DataAnnotations.ValidationResult.Success
                        : new System.ComponentModel.DataAnnotations.ValidationResult("No tenant");
            }
        }
        """;

    // The compile check comes first: a compile error in the generated code explains a snapshot
    // difference far better than the snapshot diff does. Generated code is held to a higher bar
    // than the test input: consumers build with warnings as errors and nullable analysis on, so
    // any warning in a generated tree (a CS86xx from a mis-annotated array type, say) is a
    // failure here, while the input source only has to be free of errors.
    protected static async Task VerifyGeneratedSource(string input, LanguageVersion languageVersion = LanguageVersion.LatestMajor)
    {
        var verification = SnapshotTestHelper.Verify<StronglyTypedPrimitiveGenerator>(
            input,
            languageVersion,
            out var compilation
        );

        var diagnostics = compilation.GetDiagnostics(TestContext.Current.CancellationToken);
        Assert.Empty(diagnostics.Where(d => d.Severity > DiagnosticSeverity.Warning));
        Assert.Empty(diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning && d.Location.SourceTree?.FilePath != "Program.cs"));

        await verification;
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
            public readonly partial record struct Foo([InstanceOf(typeof(string))] string Value);

            public sealed class InstanceOfAttribute(System.Type type) : ValidationAttribute
            {
                public override bool IsValid(object? value) => type.IsInstanceOfType(value);
            }
            """);
}

public class Validation_attributes_overriding_only_the_context_IsValid : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([NotBlank] string Value);

            public sealed class NotBlankAttribute : ValidationAttribute
            {
                protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
                    => value is string text && text.Trim().Length == 0
                        ? new ValidationResult($"The {validationContext.MemberName} field must not be blank.")
                        : ValidationResult.Success;
            }
            """);
}

public class Validation_attributes_with_null_arguments : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([Check((string?)null), Check((int[]?)null), Check(new[] { "a", null })] string Value);

            [System.AttributeUsage(System.AttributeTargets.Parameter, AllowMultiple = true)]
            public sealed class CheckAttribute : ValidationAttribute
            {
                public CheckAttribute(string? name) { }

                public CheckAttribute(System.Type? type) { }

                public CheckAttribute(int[]? steps) { }

                public CheckAttribute(string?[] names) { }

                public override bool IsValid(object? value) => true;
            }
            """);
}

public class Validation_attribute_type_names_survive_a_shadowing_namespace : ValidationAttributeTestBase
{
    // SomeNamespace.Rules shadows Rules inside the generated `namespace SomeNamespace;`, so the
    // attribute type, the enum member and the typeof argument, which the generator writes fully
    // qualified, only resolve when rooted with global::. The user code reaches them through a
    // using directive, which the shadowing does not affect.
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using Rules;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([Positive(Mode.Strict, typeof(Marker))] int Value);
            }

            namespace Rules
            {
                public enum Mode { Lenient, Strict }

                public sealed class Marker;

                public sealed class PositiveAttribute(Mode mode, System.Type marker) : System.ComponentModel.DataAnnotations.ValidationAttribute
                {
                    public Mode Mode { get; } = mode;

                    public System.Type Marker { get; } = marker;

                    public override bool IsValid(object? value) => value is int number && number > 0;
                }
            }

            namespace SomeNamespace.Rules
            {
                public static class Shadow;
            }
            """);
}

public class Validation_attributes_with_keyword_identifiers : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource("""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([Keyworded(Mode.@default, @class = 1)] int Value);

            public enum Mode { @default, @event }

            public sealed class KeywordedAttribute(Mode mode) : ValidationAttribute
            {
                public Mode Mode { get; } = mode;

                public int @class { get; set; }

                public override bool IsValid(object? value) => true;
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

public class Context_validation_attributes_are_excluded_from_IsValueValid : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource($$"""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([Required, TenantScoped, CustomValidation(typeof(FooRules), nameof(FooRules.Validate))] string Value);

                public static class FooRules
                {
                    public static ValidationResult? Validate(string value, ValidationContext context)
                        => value.StartsWith("x") ? ValidationResult.Success : new ValidationResult("Must start with x");
                }
            }

            {{FakeContextValidationAttribute}}
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
