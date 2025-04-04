using Egil.StronglyTypedPrimitives;

namespace Examples
{
    [StronglyTyped]
    public readonly partial record struct StronglyTypedStringWithConstraints(string Value)
    {
        public static bool IsValueValid(string value, bool throwIfInvalid)
        {
            if (value is { Length: > 5 })
                return true;

            if (throwIfInvalid)
                throw new ArgumentException("Value must be at least 6 characters long", nameof(value));

            return false;
        }
    }
}