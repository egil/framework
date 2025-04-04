using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Egil.StronglyTypedPrimitives;

public abstract class StronglyTypedPrimitiveGeneratorTestBase
{
    public static TheoryData<string, LanguageVersion> UnderlyingTypes { get; } =
        new()
        {
            { "string", LanguageVersion.LatestMajor },
            { "int", LanguageVersion.LatestMajor },
            { "System.Guid", LanguageVersion.LatestMajor },
            { "System.DateTime", LanguageVersion.LatestMajor },
            { "System.DateTimeOffset", LanguageVersion.LatestMajor },
            { "System.TimeSpan", LanguageVersion.LatestMajor },
            { "System.DateOnly", LanguageVersion.LatestMajor },
            { "System.TimeOnly", LanguageVersion.LatestMajor },
            { "decimal", LanguageVersion.LatestMajor },
            { "byte", LanguageVersion.LatestMajor },
            //{ "char", LanguageVersion.LatestMajor },
            //"char",
            //"bool",
        };

    [Fact]
    public async Task StandardX()
    {
        var input = $$"""
            using Egil.StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(string Value);
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

public class Plain_type : StronglyTypedPrimitiveGeneratorTestBase
{
    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Test(string underlyingType, LanguageVersion languageVersion)
    {
        var input = $$"""
            using Egil.StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo({{underlyingType}} Value);
            """;

        await SnapshotTestHelper.Verify<StronglyTypedPrimitiveGenerator>(
            input,
            languageVersion,
            out var compilation
        );

        Assert.Empty(
            compilation
                .GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity > DiagnosticSeverity.Warning)
        );
    }
}

public class No_namespace : StronglyTypedPrimitiveGeneratorTestBase
{
    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Test(string underlyingType, LanguageVersion languageVersion)
    {
        var input = $$"""
            using Egil.StronglyTypedPrimitives;

            [StronglyTyped]
            public readonly partial record struct Foo({{underlyingType}} Value);
            """;

        await SnapshotTestHelper.Verify<StronglyTypedPrimitiveGenerator>(
            input,
            languageVersion,
            out var compilation
        );

        Assert.Empty(
            compilation
                .GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity > DiagnosticSeverity.Warning)
        );
    }
}

public class Alt_type_param_name : StronglyTypedPrimitiveGeneratorTestBase
{
    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Test(string underlyingType, LanguageVersion languageVersion)
    {
        var input = $$"""
            using Egil.StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo({{underlyingType}} Data);
            """;

        await SnapshotTestHelper.Verify<StronglyTypedPrimitiveGenerator>(
            input,
            languageVersion,
            out var compilation
        );

        Assert.Empty(
            compilation
                .GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity > DiagnosticSeverity.Warning)
        );
    }
}

public class Custom_IsValueValid : StronglyTypedPrimitiveGeneratorTestBase
{
    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Test(string underlyingType, LanguageVersion languageVersion)
    {
        var input = $$"""
            using Egil.StronglyTypedPrimitives;

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
            out var compilation
        );

        Assert.Empty(
            compilation
                .GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity > DiagnosticSeverity.Warning)
        );
    }
}

public class Custom_IParsable_Parse : StronglyTypedPrimitiveGeneratorTestBase
{
    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Test(string underlyingType, LanguageVersion languageVersion)
    {
        var input = $$"""
            #nullable enable
            using System;
            using Egil.StronglyTypedPrimitives;

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
            out var compilation
        );

        Assert.Empty(
            compilation
                .GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity > DiagnosticSeverity.Warning)
        );
    }
}

public class Custom_IParsable_TryParse : StronglyTypedPrimitiveGeneratorTestBase
{
    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Test(
        string underlyingType,
        LanguageVersion languageVersion)
    {
        var input = $$"""
            #nullable enable
            using System;
            using Egil.StronglyTypedPrimitives;

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
            out var compilation
        );

        Assert.Empty(
            compilation
                .GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity > DiagnosticSeverity.Warning)
        );
    }
}

public class Custom_IFormattable_ToString : StronglyTypedPrimitiveGeneratorTestBase
{
    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Test(
        string underlyingType,
        LanguageVersion languageVersion)
    {
        var input = $$"""
            #nullable enable
            using Egil.StronglyTypedPrimitives;

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
            out var compilation
        );

        Assert.Empty(
            compilation
                .GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity > DiagnosticSeverity.Warning)
        );
    }
}

public class Custom_IFormattable_ToString_with_format : StronglyTypedPrimitiveGeneratorTestBase
{
    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Test(
        string underlyingType,
        LanguageVersion languageVersion
    )
    {
        var input = $$"""
            #nullable enable
            using Egil.StronglyTypedPrimitives;

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
            out var compilation
        );

        Assert.Empty(
            compilation
                .GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity > DiagnosticSeverity.Warning)
        );
    }
}

public class Custom_IFormattable_ToString_with_format_and_formatProvider : StronglyTypedPrimitiveGeneratorTestBase
{
    [Theory, MemberData(nameof(UnderlyingTypes))]
    public async Task Test(
        string underlyingType,
        LanguageVersion languageVersion)
    {
        var input = $$"""
            #nullable enable
            using Egil.StronglyTypedPrimitives;

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
            out var compilation
        );

        Assert.Empty(
            compilation
                .GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity > DiagnosticSeverity.Warning)
        );
    }
}
