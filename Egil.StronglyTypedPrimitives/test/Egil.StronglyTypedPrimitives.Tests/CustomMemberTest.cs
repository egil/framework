using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Egil.StronglyTypedPrimitives;

public class Custom_Create
{
    [Fact]
    public async Task Test()
    {
        var input = """
            using Egil.StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(int Value)
            {
                public static Foo Create(int value) => new Foo(value);
            }
            """;

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

public class Custom_JsonConverter
{
    [Fact]
    public async Task Test()
    {
        var input = """
            using Egil.StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            [System.Text.Json.Serialization.JsonConverter(typeof(StronglyTypedJsonConverter<Foo, int>))]
            public readonly partial record struct Foo(int Value);
            """;

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

public class Custom_Create_non_public
{
    [Fact]
    public async Task Test()
    {
        var input = """
            using Egil.StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(int Value)
            {
                private static Foo Create(int value) => new Foo(value);
            }
            """;

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

public class Custom_Value_with_non_public_getter
{
    [Fact]
    public async Task Test()
    {
        var input = """
            using Egil.StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(int Value)
            {
                public int Value { private get; init; } = Value;
            }
            """;

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

public class Custom_Empty_field
{
    [Fact]
    public async Task Test()
    {
        var input = """
            using Egil.StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(int Value)
            {
                public static readonly Foo Empty = new(42);
            }
            """;

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

public class Custom_Empty_property
{
    [Fact]
    public async Task Test()
    {
        var input = """
            using Egil.StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(int Value)
            {
                public static Foo Empty => new(42);
            }
            """;

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