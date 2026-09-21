using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Egil.StronglyTypedPrimitives;

public class ValidationAttributeDiagnosticTest
{
    [Fact]
    public void Warns_when_a_user_written_IsValueValid_leaves_validation_attributes_unused()
    {
        var input = """
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([EmailAddress] string Value)
            {
                public static bool IsValueValid(string value, bool throwIfInvalid)
                    => true;
            }
            """;

        var result = SnapshotTestHelper.RunGenerator<StronglyTypedPrimitiveGenerator>(input, out var compilation);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("STP002", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(
            "The validation attribute 'System.ComponentModel.DataAnnotations.EmailAddressAttribute' on 'Value' has no effect because 'Foo' declares its own IsValueValid method. Remove the attribute, or remove the method to let the generator validate the value with the attributes.",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal("EmailAddress", diagnostic.Location.SourceTree?.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity > DiagnosticSeverity.Warning));
        Assert.DoesNotContain("valueValidator", Assert.Single(result.GeneratedTrees).ToString());
    }

    [Fact]
    public void Warns_when_an_async_validation_attribute_has_no_ValidateAsync_to_run_in()
    {
        var input = $$"""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([Required, RemoteCheck] string Value);

                public sealed class RemoteCheckAttribute : AsyncValidationAttribute
                {
                }
            }

            {{ValidationAttributeTestBase.FakeAsyncValidationApi}}
            """;

        var result = SnapshotTestHelper.RunGenerator<StronglyTypedPrimitiveGenerator>(input, out var compilation);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("STP003", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(
            "The async validation attribute 'SomeNamespace.RemoteCheckAttribute' on 'Value' is not part of the value invariant of 'Foo' because async validation cannot run where the invariant is checked, so nothing this generator emits evaluates it. ASP.NET Core validation still evaluates it on the 'Value' property; for it to run through ValidateAsync, for example under Validator.TryValidateObjectAsync, declare System.ComponentModel.DataAnnotations.IAsyncValidatableObject on the partial declaration of 'Foo'.",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal("RemoteCheck", diagnostic.Location.SourceTree?.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity > DiagnosticSeverity.Warning));
    }

    [Fact]
    public void Warns_when_a_validation_attribute_requires_a_ValidationContext()
    {
        var input = $$"""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([Required, TenantScoped] string Value);
            }

            {{ValidationAttributeTestBase.FakeContextValidationAttribute}}
            """;

        var result = SnapshotTestHelper.RunGenerator<StronglyTypedPrimitiveGenerator>(input, out var compilation);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("STP005", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(
            "The validation attribute 'SomeNamespace.TenantScopedAttribute' on 'Value' is not part of the value invariant of 'Foo' because it requires a ValidationContext. It is evaluated only through IValidatableObject.Validate when 'Foo' declares that interface.",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal("TenantScoped", diagnostic.Location.SourceTree?.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity > DiagnosticSeverity.Warning));
    }

    [Fact]
    public void Does_not_warn_about_an_async_validation_attribute_when_IAsyncValidatableObject_is_declared()
    {
        var input = $$"""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([Required, RemoteCheck] string Value) : IAsyncValidatableObject;

                public sealed class RemoteCheckAttribute : AsyncValidationAttribute
                {
                }
            }

            {{ValidationAttributeTestBase.FakeAsyncValidationApi}}
            """;

        var result = SnapshotTestHelper.RunGenerator<StronglyTypedPrimitiveGenerator>(input, out var compilation);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity > DiagnosticSeverity.Warning));
    }

    [Fact]
    public void Warns_that_CustomValidation_requires_a_ValidationContext()
    {
        var input = """
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([CustomValidation(typeof(FooRules), nameof(FooRules.Validate))] string Value);

            public static class FooRules
            {
                public static ValidationResult? Validate(string value) => ValidationResult.Success;
            }
            """;

        var result = SnapshotTestHelper.RunGenerator<StronglyTypedPrimitiveGenerator>(input, out var compilation);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("STP005", diagnostic.Id);
        Assert.Equal(
            "The validation attribute 'System.ComponentModel.DataAnnotations.CustomValidationAttribute' on 'Value' is not part of the value invariant of 'Foo' because it requires a ValidationContext. It is evaluated only through IValidatableObject.Validate when 'Foo' declares that interface.",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity > DiagnosticSeverity.Warning));
        Assert.DoesNotContain("CustomValidationAttribute", Assert.Single(result.GeneratedTrees).ToString());
    }

    // The reference assemblies of the framework (net10.0 and net11.0) leave out the
    // RequiresValidationContext override of CustomValidationAttribute that the implementation
    // assembly has, and a consumer's build compiles against the reference assemblies. The
    // generator therefore cannot rely on seeing the override for this attribute.
    [Fact]
    public void CustomValidation_requires_a_context_when_compiled_against_reference_assemblies()
    {
        var input = """
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([CustomValidation(typeof(FooRules), nameof(FooRules.Validate))] string Value);

            public static class FooRules
            {
                public static ValidationResult? Validate(string value, ValidationContext context) => ValidationResult.Success;
            }
            """;

        var result = SnapshotTestHelper.RunGeneratorAgainstFrameworkReferenceAssemblies<StronglyTypedPrimitiveGenerator>(input, out var compilation);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("STP005", diagnostic.Id);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity > DiagnosticSeverity.Warning));
        Assert.DoesNotContain("CustomValidationAttribute", Assert.Single(result.GeneratedTrees).ToString());
    }

    [Fact]
    public void Does_not_warn_when_the_attributes_drive_the_generated_IsValueValid()
    {
        var input = """
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo([EmailAddress] string Value);
            """;

        var result = SnapshotTestHelper.RunGenerator<StronglyTypedPrimitiveGenerator>(input, out var compilation);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity > DiagnosticSeverity.Warning));
    }
}
