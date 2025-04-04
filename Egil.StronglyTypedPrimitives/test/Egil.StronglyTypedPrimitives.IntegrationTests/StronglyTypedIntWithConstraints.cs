using Egil.StronglyTypedPrimitives;

namespace Examples
{
    [StronglyTyped]
    public readonly partial record struct StronglyTypedIntWithConstraints(int Value)
    {
        public static bool IsValueValid(int value, bool throwIfInvalid)
        {
            if (value > 5)
                return true;

            if (throwIfInvalid)
                throw new ArgumentException("Value must be at larger than 5", nameof(value));

            return false;
        }
    }
}