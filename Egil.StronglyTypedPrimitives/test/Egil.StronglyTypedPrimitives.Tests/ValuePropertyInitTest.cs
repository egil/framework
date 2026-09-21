using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Egil.StronglyTypedPrimitives;

// Before C# 14 the generated Value property has an explicit backing field, and the init accessor
// assigns to it by name. The snapshot tests only prove the source compiles, so this test runs the
// accessor: a field whose name is shadowed by the accessor's implicit `value` parameter compiles
// fine but never gets written, which silently turns `foo with { Value = v }` into a copy.
public class Value_property_init_accessor_before_the_field_keyword
{
    [Fact]
    public void With_expression_replaces_the_wrapped_value()
    {
        var input = """
            using Egil.StronglyTypedPrimitives;

            namespace SomeNamespace;

            [StronglyTyped]
            public readonly partial record struct Foo(int Value)
            {
                public static bool IsValueValid(int value, bool throwIfInvalid) => value > 5;

                public static int Replace(Foo foo, int value) => (foo with { Value = value }).Value;
            }
            """;

        SnapshotTestHelper.RunGenerator<StronglyTypedPrimitiveGenerator>(input, LanguageVersion.CSharp12, out var compilation);
        var foo = LoadType(compilation, "SomeNamespace.Foo");

        var replaced = foo.GetMethod("Replace", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [Activator.CreateInstance(foo, 6), 7]);

        Assert.Equal(7, replaced);
    }

    private static Type LoadType(Compilation compilation, string fullName)
    {
        using var assemblyStream = new MemoryStream();
        var emitResult = compilation.Emit(assemblyStream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        return Assembly.Load(assemblyStream.ToArray()).GetType(fullName, throwOnError: true)!;
    }
}
