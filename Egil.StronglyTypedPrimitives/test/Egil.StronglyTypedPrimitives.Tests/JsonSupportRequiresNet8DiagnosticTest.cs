using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Egil.StronglyTypedPrimitives;

public class JsonSupportRequiresNet8DiagnosticTest
{
    // Stands in for the netstandard2.0 asset of the Abstractions assembly, which ships the
    // attribute and the interfaces but not StronglyTypedJsonConverter<,>; the helper leaves the
    // real assembly out of the compilation.
    private const string StronglyTypedAttributeStub = """
        namespace Egil.StronglyTypedPrimitives
        {
            [System.AttributeUsage(System.AttributeTargets.Struct)]
            public sealed class StronglyTypedAttribute : System.Attribute { }

            public interface IStronglyTypedPrimitive { }

            public interface IStronglyTypedPrimitive<TPrimitiveType> : IStronglyTypedPrimitive
            {
                static abstract bool IsValueValid(TPrimitiveType value, bool throwIfInvalid);
            }

            public interface IStronglyTypedPrimitive<TSelf, TPrimitiveType> : IStronglyTypedPrimitive<TPrimitiveType>
                where TSelf : IStronglyTypedPrimitive<TSelf, TPrimitiveType>
            {
                TPrimitiveType Value { get; }

                static abstract TSelf Create(TPrimitiveType value);
            }
        }
        """;

    [Fact]
    public void Warns_and_emits_no_converter_attribute_when_the_shared_converter_is_unavailable()
    {
        var input = $$"""
            using Egil.StronglyTypedPrimitives;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo(int Value);
            }

            {{StronglyTypedAttributeStub}}
            """;

        var result = SnapshotTestHelper.RunGenerator<StronglyTypedPrimitiveGenerator>(input, out var compilation, referenceAbstractions: false);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("STP002", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("requires targeting net8.0 or later", diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal("Foo", diagnostic.Location.SourceTree?.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
        var generatedSource = Assert.Single(result.GeneratedTrees).GetText(TestContext.Current.CancellationToken).ToString();
        Assert.DoesNotContain("JsonConverterAttribute", generatedSource);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity > DiagnosticSeverity.Warning));
    }

    [Fact]
    public void Does_not_warn_when_the_user_declares_a_converter_attribute_themselves()
    {
        var input = $$"""
            using Egil.StronglyTypedPrimitives;

            namespace SomeNamespace
            {
                [StronglyTyped]
                [System.Text.Json.Serialization.JsonConverter(typeof(FooConverter))]
                public readonly partial record struct Foo(int Value);

                public sealed class FooConverter : System.Text.Json.Serialization.JsonConverter<Foo>
                {
                    public override Foo Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options) => new(reader.GetInt32());
                    public override void Write(System.Text.Json.Utf8JsonWriter writer, Foo value, System.Text.Json.JsonSerializerOptions options) => writer.WriteNumberValue(value.Value);
                }
            }

            {{StronglyTypedAttributeStub}}
            """;

        var result = SnapshotTestHelper.RunGenerator<StronglyTypedPrimitiveGenerator>(input, out var compilation, referenceAbstractions: false);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity > DiagnosticSeverity.Warning));
    }
}