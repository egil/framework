using Egil.StronglyTypedPrimitives;

namespace Examples
{
    [StronglyTyped]
    public readonly partial record struct StronglyTypedDateTimeOffset(DateTimeOffset Value);
}