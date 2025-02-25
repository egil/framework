namespace StronglyTypedPrimitives;

public class StronglyTypedPrimitiveGeneratorTest
{
    public static TheoryData<string> UnderlyingTypes { get; } = new TheoryData<string>
    {
        "string",
        "int",
        "System.Guid",
        "System.DateTime",
        "System.DateTimeOffset",
        "System.TimeSpan",
        "decimal",
        "byte",
        //"char",
        //"bool",
    };

    //[Fact]
    //public async Task StandardX()
    //{
    //    var input = $$"""
    //        using StronglyTypedPrimitives;

    //        namespace SomeNamespace;

    //        [StronglyTyped]
    //        public readonly partial record struct Foo(char Value);
    //        """;

    //    var (diagnostics, output) = SnapshotTestHelper.GetGeneratedOutput<StronglyTypedPrimitiveGenerator>(
    //        input,
    //        includeAttributes: false);

    //    await Verify(output, extension: "cs");
    //    Assert.Empty(diagnostics);
    //}

    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Standard(string underlyingType)
    {
        var input = $$"""
            using StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo({{underlyingType}} Value);
            """;

        var (diagnostics, output) = SnapshotTestHelper.GetGeneratedOutput<StronglyTypedPrimitiveGenerator>(
            input,
            includeAttributes: false);

        await Verify(output, extension: "cs").UseParameters(underlyingType);
        Assert.Empty(diagnostics);
    }

    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task No_namespace(string underlyingType)
    {
        var input = $$"""
            using StronglyTypedPrimitives;

            [StronglyTyped]
            public readonly partial record struct Foo({{underlyingType}} Value);
            """;

        var (diagnostics, output) = SnapshotTestHelper.GetGeneratedOutput<StronglyTypedPrimitiveGenerator>(
            input,
            includeAttributes: false);

        await Verify(output, extension: "cs").UseParameters(underlyingType);
        Assert.Empty(diagnostics);
    }

    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Alt_type_param_name(string underlyingType)
    {
        var input = $$"""
            using StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo({{underlyingType}} Data);
            """;

        var (diagnostics, output) = SnapshotTestHelper.GetGeneratedOutput<StronglyTypedPrimitiveGenerator>(
            input,
            includeAttributes: false);

        await Verify(output, extension: "cs").UseParameters(underlyingType);
        Assert.Empty(diagnostics);
    }

    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Validator_attributes(string underlyingType)
    {
        var input = $$"""
            using System.ComponentModel.DataAnnotations;
            using StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(
                [Range(50, 100), Required]
                [RegularExpression(@"^[a-zA-Z''-'\s]{1,40}$")]
                [DeniedValues("foo", "bar")] {{underlyingType}} Value);
            """;

        var (diagnostics, output) = SnapshotTestHelper.GetGeneratedOutput<StronglyTypedPrimitiveGenerator>(
            input,
            includeAttributes: false);

        await Verify(output, extension: "cs").UseParameters(underlyingType);
        Assert.Empty(diagnostics);
    }

    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Custom_IsValueValid(string underlyingType)
    {
        var input = $$"""
            using StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo({{underlyingType}} Value)
            {
                public static bool IsValueValid({{underlyingType}} value, bool throwIfInvalid) 
                    => true;
            }
            """;

        var (diagnostics, output) = SnapshotTestHelper.GetGeneratedOutput<StronglyTypedPrimitiveGenerator>(
            input,
            includeAttributes: false);

        await Verify(output, extension: "cs").UseParameters(underlyingType);
        Assert.Empty(diagnostics);
    }

    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Custom_IParsable_Parse(string underlyingType)
    {
        var input = $$"""
            #nullable enable
            using System;
            using StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo({{underlyingType}} Value)
            {
                public static Foo Parse(string? s, IFormatProvider? provider)
                {
                    return Foo.Empty;
                }
            }
            """;

        var (diagnostics, output) = SnapshotTestHelper.GetGeneratedOutput<StronglyTypedPrimitiveGenerator>(
            input,
            includeAttributes: false);

        await Verify(output, extension: "cs").UseParameters(underlyingType);
        Assert.Empty(diagnostics);
    }

    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Custom_IParsable_TryParse(string underlyingType)
    {
        var input = $$"""
            #nullable enable
            using System;
            using StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo({{underlyingType}} Value)
            {
                public static bool TryParse(string? s, IFormatProvider? provider, out Foo result)
                {
                    result = Foo.Empty;
                    return true;
                }
            }
            """;

        var (diagnostics, output) = SnapshotTestHelper.GetGeneratedOutput<StronglyTypedPrimitiveGenerator>(
            input,
            includeAttributes: false);

        await Verify(output, extension: "cs").UseParameters(underlyingType);
        Assert.Empty(diagnostics);
    }

    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Custom_IFormattable_ToString(string underlyingType)
    {
        var input = $$"""
            #nullable enable
            using StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo({{underlyingType}} Value)
            {
                public override string ToString() => "";
            }
            """;

        var (diagnostics, output) = SnapshotTestHelper.GetGeneratedOutput<StronglyTypedPrimitiveGenerator>(
            input,
            includeAttributes: false);

        await Verify(output, extension: "cs").UseParameters(underlyingType);
        Assert.Empty(diagnostics);
    }

    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Custom_IFormattable_ToString_with_format(string underlyingType)
    {
        var input = $$"""
            #nullable enable
            using StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo({{underlyingType}} Value)
            {
                public string ToString(string? format) => "";
            }
            """;

        var (diagnostics, output) = SnapshotTestHelper.GetGeneratedOutput<StronglyTypedPrimitiveGenerator>(
            input,
            includeAttributes: false);

        await Verify(output, extension: "cs").UseParameters(underlyingType);
        Assert.Empty(diagnostics);
    }

    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Custom_IFormattable_ToString_with_format_and_formatProvider(string underlyingType)
    {
        var input = $$"""
            #nullable enable
            using StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo({{underlyingType}} Value)
            {
                public string ToString(string? format, global::System.IFormatProvider? formatProvider) => "";
            }
            """;

        var (diagnostics, output) = SnapshotTestHelper.GetGeneratedOutput<StronglyTypedPrimitiveGenerator>(
            input,
            includeAttributes: false);

        await Verify(output, extension: "cs").UseParameters(underlyingType);
        Assert.Empty(diagnostics);
    }
}
