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