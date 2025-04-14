using Egil.StronglyTypedPrimitives;

namespace Examples
{
    [StronglyTyped]
    public readonly partial record struct StronglyTypedDecimal(decimal Value);
}