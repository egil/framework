using Egil.StronglyTypedPrimitives;

namespace Examples
{
    [StronglyTyped]
    public readonly partial record struct StronglyTypedDateOnly(DateOnly Value);
}