using Egil.StronglyTypedPrimitives;

namespace Examples
{
    [StronglyTyped]
    public readonly partial record struct StronglyTypedDateTime(DateTime Value);
}