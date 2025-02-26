namespace StronglyTypedPrimitives;

public interface IStronglyTypedPrimitive
{
}

public interface IStronglyTypedPrimitive<TPrimitiveType> : IStronglyTypedPrimitive
{
#if NET6_0_OR_GREATER
    static abstract bool IsValueValid(TPrimitiveType value, bool throwIfInvalid);
#endif
}