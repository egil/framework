using Egil.StronglyTypedPrimitives;

namespace Examples
{
    [StronglyTyped]
    public readonly partial record struct StronglyTypedTimeOnly(TimeOnly Value);
}