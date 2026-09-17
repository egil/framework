using System.Reflection;

namespace Egil.Orleans.Messaging.State;

/// <summary>
/// Detects the optional contracts a state type may implement, without requiring the
/// type to satisfy their constraints.
/// </summary>
internal static class StateContract
{
    /// <summary>
    /// Reports whether <paramref name="stateType"/> implements
    /// <see cref="IStateDefault{TSelf}"/> closed over itself.
    /// </summary>
    /// <remarks>
    /// The interface constrains its own argument (<c>TSelf : IStateDefault&lt;TSelf&gt;</c>),
    /// so <c>typeof(IStateDefault&lt;&gt;).MakeGenericType(stateType)</c> throws for every
    /// type that does not already implement it — which is the answer being asked for.
    /// Scanning the implemented interfaces asks the question without constructing the type.
    /// </remarks>
    public static bool ImplementsStateDefault(Type stateType)
        => Array.Exists(stateType.GetInterfaces(), contract =>
            contract.IsGenericType
            && contract.GetGenericTypeDefinition() == typeof(IStateDefault<>)
            && contract.GenericTypeArguments[0] == stateType);
}

/// <summary>
/// Per-state-type cache of the optional contracts a state type may implement:
/// <see cref="IStateDefault{TSelf}"/>, <see cref="IConfigurableState"/>, and a public
/// parameterless constructor.
/// </summary>
/// <remarks>
/// Resolving these costs reflection, and a static generic type runs its initializer
/// once per <typeparamref name="T"/> for the life of the process. Grain activation is
/// on the hot path and every activation of a grain type asks the same questions of the
/// same state type, so the answers are computed once here rather than per activation.
/// </remarks>
internal static class StateContract<T>
    where T : class, IEquatable<T>
{
    /// <summary>
    /// Creates the state type's own default, or <see langword="null"/> when it does not
    /// implement <see cref="IStateDefault{TSelf}"/>.
    /// </summary>
    public static readonly Func<IGrainContext, T>? CreateDefault = BuildCreateDefault();

    /// <summary>
    /// Restores transient dependencies on an adopted instance, or <see langword="null"/>
    /// when the state type does not implement <see cref="IConfigurableState"/>.
    /// </summary>
    public static readonly Action<T, IGrainContext>? Configure =
        typeof(IConfigurableState).IsAssignableFrom(typeof(T))
            ? static (state, context) => ((IConfigurableState)state).Configure(context)
            : null;

    /// <summary>
    /// Creates an instance through the public parameterless constructor, or
    /// <see langword="null"/> when the state type has none.
    /// </summary>
    public static readonly Func<T>? CreateInstance =
        typeof(T).GetConstructor(Type.EmptyTypes) is null
            ? null
            : static () => Activator.CreateInstance<T>();

    private static Func<IGrainContext, T>? BuildCreateDefault()
    {
        if (!StateContract.ImplementsStateDefault(typeof(T)))
        {
            return null;
        }

        // IStateDefault<TSelf> declares CreateDefault as a static abstract member, which
        // can only be called through a type parameter constrained to the interface. T is
        // not so constrained here, so the call is reached through a helper that is, and
        // the resulting delegate is cached in place of the reflection.

        var builder = typeof(StateContract<T>)
            .GetMethod(nameof(BuildCreateDefaultCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(T));

        return (Func<IGrainContext, T>)builder.Invoke(null, null)!;
    }

    private static Func<IGrainContext, TSelf> BuildCreateDefaultCore<TSelf>()
        where TSelf : class, IEquatable<TSelf>, IStateDefault<TSelf>
        => static context => TSelf.CreateDefault(context);
}
