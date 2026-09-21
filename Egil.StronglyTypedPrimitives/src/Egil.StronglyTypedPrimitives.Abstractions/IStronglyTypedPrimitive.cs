namespace Egil.StronglyTypedPrimitives;

/// <summary>
/// Marker interface for strongly typed primitives.
/// </summary>
public interface IStronglyTypedPrimitive
{
}

public interface IStronglyTypedPrimitive<TPrimitiveType> : IStronglyTypedPrimitive
{
    // The netstandard2.0 asset cannot declare static abstract interface members, so there the
    // interfaces carry no static members; the generator still emits the same members on every
    // strongly typed primitive.
#if NET10_0_OR_GREATER
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

#if NET10_0_OR_GREATER
    /// <summary>
    /// Creates an instance wrapping <paramref name="value"/>, validating it the same way the constructor does.
    /// </summary>
    static abstract TSelf Create(TPrimitiveType value);

    /// <summary>
    /// Gets the instance that stands in for a missing or invalid value, for example when
    /// <c>TryParse</c> fails or JSON deserialization reads a value that is not valid.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>default(TSelf)</c>, which is what the generated <c>Empty</c> field holds.
    /// When a strongly typed primitive declares its own <c>Empty</c>, the generator forwards it
    /// here so generic code such as <c>StronglyTypedJsonConverter&lt;TSelf, TPrimitive&gt;</c>
    /// observes the replacement.
    /// </remarks>
    static virtual TSelf Empty => default!;
#endif
}