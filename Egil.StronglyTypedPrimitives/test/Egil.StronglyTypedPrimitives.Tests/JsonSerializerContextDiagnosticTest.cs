using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Egil.StronglyTypedPrimitives;

public class JsonSerializerContextDiagnosticTest
{
    // The ways a context can spell its base type. The System.Text.Json source generator accepts
    // all of them, so the STP001 detection must not depend on how the base type is written.
    public static TheoryData<string, string> BaseTypeSpellings { get; } = new()
    {
        { "using System.Text.Json.Serialization;", "JsonSerializerContext" },
        { "", "System.Text.Json.Serialization.JsonSerializerContext" },
        { "", "global::System.Text.Json.Serialization.JsonSerializerContext" },
        { "using ContextAlias = System.Text.Json.Serialization.JsonSerializerContext;", "ContextAlias" },
    };

    // A hand-written stand-in for what the System.Text.Json source generator would emit; the
    // generator only needs a class deriving from JsonSerializerContext to exist in the compilation.
    private static string JsonSerializerContext(string baseType) => $$"""
        public partial class AppJsonContext : {{baseType}}
        {
            public AppJsonContext() : base(null) { }

            protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;

            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
        }
        """;

    [Theory, MemberData(nameof(BaseTypeSpellings))]
    public void Warns_when_a_JsonSerializerContext_cannot_see_the_generated_converter(string usings, string baseType)
    {
        var input = $$"""
            using Egil.StronglyTypedPrimitives;
            {{usings}}

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(int Value);

            {{JsonSerializerContext(baseType)}}
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

            {{JsonSerializerContext("System.Text.Json.Serialization.JsonSerializerContext")}}
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

    [Fact]
    public void Does_not_warn_for_a_class_with_an_unrelated_base_type()
    {
        var input = """
            using Egil.StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(int Value);

            public class NotAContext : System.Exception;
            """;

        var result = SnapshotTestHelper.RunGenerator<StronglyTypedPrimitiveGenerator>(input, out var compilation);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity > DiagnosticSeverity.Warning));
    }
}