using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Egil.StronglyTypedPrimitives;

internal static class SemanticModelExtensions
{
    internal static bool HasOperatorOverload(this IEnumerable<ISymbol> symbols, INamedTypeSymbol targetSymbol, string operatorName)
         => symbols
            .OfType<IMethodSymbol>()
            .Any(m => m.Name == operatorName && m.Parameters.Length == 2 && m.Parameters.All(x => x.Type.Equals(targetSymbol, SymbolEqualityComparer.Default)));

    internal static bool IsTypeImplementingInterface(this SemanticModel semanticModel, ITypeSymbol target, INamedTypeSymbol @interface)
        => target.AllInterfaces.Contains(@interface, SymbolEqualityComparer.Default);

    internal static IEnumerable<ISymbol> GetUnimplementedSymbols(this SemanticModel semanticModel, HashSet<ISymbol> targetMembers, INamedTypeSymbol @interface)
    {
        foreach (var interfaceMember in @interface.GetMembers())
        {
            if (!targetMembers.Contains(interfaceMember, SymbolStructureEqualityComparer.Default))
            {
                yield return interfaceMember;
            }
        }
    }

    private sealed class SymbolStructureEqualityComparer : IEqualityComparer<ISymbol?>
    {
        public static readonly SymbolStructureEqualityComparer Default = new SymbolStructureEqualityComparer();

        public bool Equals(ISymbol? x, ISymbol? y)
        {
            if (x is IMethodSymbol method1 && y is IMethodSymbol method2)
            {
                return method1.Name == method2.Name &&
                       method1.ReturnType.Equals(method2.ReturnType, SymbolEqualityComparer.Default) &&
                       method1.Parameters.Length == method2.Parameters.Length &&
                       method1.Parameters.Zip(method2.Parameters, (p1, p2) => p1.Type.Equals(p2.Type, SymbolEqualityComparer.Default)).All(x => x);
            }

            if (x is IPropertySymbol property1 && y is IPropertySymbol property2)
            {
                return property1.Name == property2.Name &&
                       property1.Type.Equals(property2.Type, SymbolEqualityComparer.Default);
            }

            return false;
        }

        public int GetHashCode(ISymbol? obj)
            => obj is not null
            ? SymbolEqualityComparer.Default.GetHashCode()
            : 0;
    }
}
