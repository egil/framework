namespace Egil.StronglyTypedPrimitives;

/// <summary>
/// Marker interface for strongly typed primitives.
/// </summary>
public interface IStronglyTypedPrimitive
{
}

public interface IStronglyTypedPrimitive<TPrimitiveType> : IStronglyTypedPrimitive
{
#if NET6_0_OR_GREATER
    static abstract bool IsValueValid(TPrimitiveType value, bool throwIfInvalid);
#endif
}

/// <summary>
/// Implemented by every generated strongly typed primitive so generic code, such as
/// <c>StronglyTypedJsonConverter&lt;TSelf, TPrimitive&gt;</c>, can read the wrapped value
/// and create instances without knowing the concrete type.
/// </summary>
/// <typeparam name="TSelf">The strongly typed primitive itself.</typeparam>
/// <typeparam name="TPrimitiveType">The wrapped primitive type.</typeparam>
public interface IStronglyTypedPrimitive<TSelf, TPrimitiveType> : IStronglyTypedPrimitive<TPrimitiveType>
    where TSelf : IStronglyTypedPrimitive<TSelf, TPrimitiveType>
{
    /// <summary>
    /// Gets the wrapped primitive value.
    /// </summary>
    TPrimitiveType Value { get; }

#if NET6_0_OR_GREATER
    /// <summary>
    /// Creates an instance wrapping <paramref name="value"/>, validating it the same way the constructor does.
    /// </summary>
    static abstract TSelf Create(TPrimitiveType value);
#endif
}