using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StronglyTypedPrimitives;

public class StronglyTypedPrimitiveGeneratorTest
{
    public static TheoryData<string, LanguageVersion> UnderlyingTypes { get; } = new()
    {
        { "string", LanguageVersion.Preview },
        { "int", LanguageVersion.Preview },
        { "System.Guid", LanguageVersion.Preview },
        { "System.DateTime", LanguageVersion.Preview },
        { "System.DateTimeOffset", LanguageVersion.Preview },
        { "System.TimeSpan", LanguageVersion.Preview },
        { "decimal", LanguageVersion.Preview },
        { "byte", LanguageVersion.Preview },
        { "string", LanguageVersion.CSharp13 },
        { "int", LanguageVersion.CSharp13 },
        { "System.Guid", LanguageVersion.CSharp13 },
        { "System.DateTime", LanguageVersion.CSharp13 },
        { "System.DateTimeOffset", LanguageVersion.CSharp13 },
        { "System.TimeSpan", LanguageVersion.CSharp13 },
        { "decimal", LanguageVersion.CSharp13 },
        { "byte", LanguageVersion.CSharp13 },
        //"char",
        //"bool",
    };

    [Fact]
    public async Task StandardX()
    {
        var input = $$"""
            using StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(string Value);
            """;

        await SnapshotTestHelper.Verify<StronglyTypedPrimitiveGenerator>(
            input,
            LanguageVersion.CSharp13,
            out var compilation);

        Assert.Empty(compilation
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity > DiagnosticSeverity.Warning));
    }

    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Standard(string underlyingType, LanguageVersion languageVersion)
    {
        var input = $$"""
            using StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo({{underlyingType}} Value);
            """;

        await SnapshotTestHelper.Verify<StronglyTypedPrimitiveGenerator>(
            input,
            languageVersion,
            out var compilation);

        Assert.Empty(compilation
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity > DiagnosticSeverity.Warning));
    }

    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task No_namespace(string underlyingType, LanguageVersion languageVersion)
    {
        var input = $$"""
            using StronglyTypedPrimitives;

            [StronglyTyped]
            public readonly partial record struct Foo({{underlyingType}} Value);
            """;

        await SnapshotTestHelper.Verify<StronglyTypedPrimitiveGenerator>(
            input,
            languageVersion,
            out var compilation);

        Assert.Empty(compilation
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity > DiagnosticSeverity.Warning));
    }

    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Alt_type_param_name(string underlyingType, LanguageVersion languageVersion)
    {
        var input = $$"""
            using StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo({{underlyingType}} Data);
            """;

        await SnapshotTestHelper.Verify<StronglyTypedPrimitiveGenerator>(
            input,
            languageVersion,
            out var compilation);

        Assert.Empty(compilation
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity > DiagnosticSeverity.Warning));
    }

    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Custom_IsValueValid(string underlyingType, LanguageVersion languageVersion)
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

        await SnapshotTestHelper.Verify<StronglyTypedPrimitiveGenerator>(
            input,
            languageVersion,
            out var compilation);

        Assert.Empty(compilation
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity > DiagnosticSeverity.Warning));
    }

    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Custom_IParsable_Parse(string underlyingType, LanguageVersion languageVersion)
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

        await SnapshotTestHelper.Verify<StronglyTypedPrimitiveGenerator>(
            input,
            languageVersion,
            out var compilation);

        Assert.Empty(compilation
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity > DiagnosticSeverity.Warning));
    }

    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Custom_IParsable_TryParse(string underlyingType, LanguageVersion languageVersion)
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

        await SnapshotTestHelper.Verify<StronglyTypedPrimitiveGenerator>(
            input,
            languageVersion,
            out var compilation);

        Assert.Empty(compilation
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity > DiagnosticSeverity.Warning));
    }

    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Custom_IFormattable_ToString(string underlyingType, LanguageVersion languageVersion)
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

        await SnapshotTestHelper.Verify<StronglyTypedPrimitiveGenerator>(
            input,
            languageVersion,
            out var compilation);

        Assert.Empty(compilation
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity > DiagnosticSeverity.Warning));
    }

    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Custom_IFormattable_ToString_with_format(string underlyingType, LanguageVersion languageVersion)
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

        await SnapshotTestHelper.Verify<StronglyTypedPrimitiveGenerator>(
            input,
            languageVersion,
            out var compilation);

        Assert.Empty(compilation
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity > DiagnosticSeverity.Warning));
    }

    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Custom_IFormattable_ToString_with_format_and_formatProvider(string underlyingType, LanguageVersion languageVersion)
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

        await SnapshotTestHelper.Verify<StronglyTypedPrimitiveGenerator>(
            input,
            languageVersion,
            out var compilation);

        Assert.Empty(compilation
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity > DiagnosticSeverity.Warning));
    }
}
