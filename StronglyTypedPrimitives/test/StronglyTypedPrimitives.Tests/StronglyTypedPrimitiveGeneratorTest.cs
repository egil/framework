namespace StronglyTypedPrimitives;

public class StronglyTypedPrimitiveGeneratorTest
{
    [Fact]
    public async Task Int_type()
    {
        var input = $$"""
            using StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(int Value);
            """;

        var (diagnostics, output) = SnapshotTestHelper.GetGeneratedOutput<StronglyTypedPrimitiveGenerator>(input, includeAttributes: false);

        Assert.Empty(diagnostics);
        await Verify(output, extension: "cs");
    }

    [Fact]
    public async Task Int_type_no_namespace()
    {
        var input = $$"""
            using StronglyTypedPrimitives;

            [StronglyTyped]
            public readonly partial record struct Foo(int Value);
            """;

        var (diagnostics, output) = SnapshotTestHelper.GetGeneratedOutput<StronglyTypedPrimitiveGenerator>(input, includeAttributes: false);

        Assert.Empty(diagnostics);
        await Verify(output, extension: "cs");
    }

    [Fact]
    public async Task Int_type_alt_value_name()
    {
        var input = $$"""
            using StronglyTypedPrimitives;

            namespace SomeNamespace;            

            [StronglyTyped]
            public readonly partial record struct Foo(int Data);
            """;

        var (diagnostics, output) = SnapshotTestHelper.GetGeneratedOutput<StronglyTypedPrimitiveGenerator>(input, includeAttributes: false);

        Assert.Empty(diagnostics);
        await Verify(output, extension: "cs");
    }

    [Fact]
    public async Task Int_type_with_validators()
    {
        var input = $$"""
            using System.ComponentModel.DataAnnotations;
            using StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(
                [Range(50, 100), Required] int Value);
            """;

        var (diagnostics, output) = SnapshotTestHelper.GetGeneratedOutput<StronglyTypedPrimitiveGenerator>(input, includeAttributes: false);

        Assert.Empty(diagnostics);
        await Verify(output, extension: "cs");
    }

    [Fact]
    public async Task String_type_with_validators()
    {
        var input = $$"""
            using System.ComponentModel.DataAnnotations;
            using StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(
                [RegularExpression(@"^[a-zA-Z''-'\s]{1,40}$"), DeniedValues("foo", "bar")] string Value);
            """;

        var (diagnostics, output) = SnapshotTestHelper.GetGeneratedOutput<StronglyTypedPrimitiveGenerator>(input, includeAttributes: false);

        Assert.Empty(diagnostics);
        await Verify(output, extension: "cs");
    }

    [Fact]
    public async Task String_type_with_custom_IsValid()
    {
        var input = $$"""
            using StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(string Value)
            {
                public static bool IsValueValid(string value, bool throwIfInvalid) 
                    => value.Length > 2;
            }
            """;

        var (diagnostics, output) = SnapshotTestHelper.GetGeneratedOutput<StronglyTypedPrimitiveGenerator>(input, includeAttributes: false);

        Assert.Empty(diagnostics);
        await Verify(output, extension: "cs");
    }
}
