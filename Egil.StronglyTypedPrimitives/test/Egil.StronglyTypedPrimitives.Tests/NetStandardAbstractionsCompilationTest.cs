using Microsoft.CodeAnalysis;

namespace Egil.StronglyTypedPrimitives;

public class NetStandardAbstractionsCompilationTest
{
    // A faithful copy of the public surface the netstandard2.0 asset of the Abstractions assembly
    // has: the attribute and the interfaces, but no static abstract members (IsValueValid, Create)
    // and no StronglyTypedJsonConverter. The helper leaves the real (net10.0) assembly out.
    private const string NetStandardAbstractions = """
        namespace Egil.StronglyTypedPrimitives
        {
            [System.AttributeUsage(System.AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
            public sealed class StronglyTypedAttribute : System.Attribute { }

            public interface IStronglyTypedPrimitive { }

            public interface IStronglyTypedPrimitive<TPrimitiveType> : IStronglyTypedPrimitive { }

            public interface IStronglyTypedPrimitive<TSelf, TPrimitiveType> : IStronglyTypedPrimitive<TPrimitiveType>
                where TSelf : IStronglyTypedPrimitive<TSelf, TPrimitiveType>
            {
                TPrimitiveType Value { get; }
            }
        }
        """;

    [Fact]
    public void Generated_code_compiles_without_static_abstract_interface_members()
    {
        var input = $$"""
            using Egil.StronglyTypedPrimitives;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Unconstrained(int Value);

                [StronglyTyped]
                public readonly partial record struct Constrained(int Value)
                {
                    public static bool IsValueValid(int value, bool throwIfInvalid) => value > 0;
                }
            }

            {{NetStandardAbstractions}}
            """;

        var result = SnapshotTestHelper.RunGenerator<StronglyTypedPrimitiveGenerator>(input, out var compilation, referenceAbstractions: false);

        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity > DiagnosticSeverity.Warning));
        var unconstrained = Assert.Single(result.GeneratedTrees, tree => tree.FilePath.EndsWith("Unconstrained.g.cs", StringComparison.Ordinal));
        Assert.Contains("public static bool IsValueValid(int value, bool throwIfInvalid)", unconstrained.GetText(TestContext.Current.CancellationToken).ToString());
        Assert.Equal(["STP004", "STP004"], result.Diagnostics.Select(d => d.Id));
    }
}