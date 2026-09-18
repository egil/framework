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
    public void Warns_when_an_async_validation_attribute_cannot_be_part_of_the_value_invariant()
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

            {{ValidationAttributeTestBase.FakeAsyncValidationAttribute}}
            """;

        var result = SnapshotTestHelper.RunGenerator<StronglyTypedPrimitiveGenerator>(input, out var compilation);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("STP003", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(
            "The async validation attribute 'SomeNamespace.RemoteCheckAttribute' on 'Value' is not evaluated by the generated IsValueValid because async validation cannot run as part of the value invariant of 'Foo'",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal("RemoteCheck", diagnostic.Location.SourceTree?.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity > DiagnosticSeverity.Warning));
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
