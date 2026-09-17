using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Egil.StronglyTypedPrimitives;

public class JsonSerializerContextDiagnosticTest
{
    // A hand-written stand-in for what the System.Text.Json source generator would emit; the
    // generator only needs a class deriving from JsonSerializerContext to exist in the compilation.
    private const string JsonSerializerContext = """
        public partial class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext
        {
            public AppJsonContext() : base(null) { }

            protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;

            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
        }
        """;

    [Fact]
    public void Warns_when_a_JsonSerializerContext_cannot_see_the_generated_converter()
    {
        var input = $$"""
            using Egil.StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(int Value);

            {{JsonSerializerContext}}
            """;

        var result = SnapshotTestHelper.RunGenerator<StronglyTypedPrimitiveGenerator>(input, out var compilation);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("STP001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains(
            "[System.Text.Json.Serialization.JsonConverter(typeof(Egil.StronglyTypedPrimitives.StronglyTypedJsonConverter<SomeNamespace.Foo, int>))]",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal("Foo", diagnostic.Location.SourceTree?.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity > DiagnosticSeverity.Warning));
    }

    [Fact]
    public void Does_not_warn_when_the_user_declares_the_converter_attribute()
    {
        var input = $$"""
            using Egil.StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            [System.Text.Json.Serialization.JsonConverter(typeof(StronglyTypedJsonConverter<Foo, int>))]
            public readonly partial record struct Foo(int Value);

            {{JsonSerializerContext}}
            """;

        var result = SnapshotTestHelper.RunGenerator<StronglyTypedPrimitiveGenerator>(input, out var compilation);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity > DiagnosticSeverity.Warning));
    }

    [Fact]
    public void Does_not_warn_without_a_JsonSerializerContext()
    {
        var input = """
            using Egil.StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(int Value);
            """;

        var result = SnapshotTestHelper.RunGenerator<StronglyTypedPrimitiveGenerator>(input, out var compilation);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity > DiagnosticSeverity.Warning));
    }
}