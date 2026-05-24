using Microsoft.Extensions.DependencyInjection;

namespace Egil.Orleans.Messaging;

/// <summary>
/// Extension methods for wiring <see cref="IStateManager{T}"/> into a grain's
/// activation lifecycle.
/// </summary>
public static class StateManagerExtensions
{
    /// <summary>
    /// Creates an <see cref="IStateManager{T}"/> for the given grain using
    /// a keyed <see cref="IStateManagerFactory{T}"/> registration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Call site:</b> Typically called once in <c>OnActivateAsync</c>:
    /// <code>
    /// [PersistentState("state")] private readonly IPersistentState&lt;MyState&gt; storage;
    /// private IStateManager&lt;MyState&gt; stateManager = default!;
    ///
    /// public override Task OnActivateAsync(CancellationToken ct)
    /// {
    ///     stateManager = this.RegisterStateManager("state", storage);
    ///     // ...
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// <b>Registration:</b> A keyed
    /// <see cref="IStateManagerFactory{T}"/> must be registered for
    /// <paramref name="storageName"/> via the provided registration helpers.
    /// Missing registrations fail fast with a descriptive
    /// <see cref="InvalidOperationException"/>.
    /// </para>
    /// </remarks>
    /// <typeparam name="TGrain">
    /// The grain type. Must implement <see cref="IGrainBase"/> so activation
    /// services can be used to resolve keyed registrations.
    /// </typeparam>
    /// <typeparam name="TState">
    /// The grain state type.
    /// </typeparam>
    /// <param name="grain">The grain instance (<c>this</c>).</param>
    /// <param name="storageName">
    /// Logical storage name used as the keyed DI registration key.
    /// </param>
    /// <param name="storage">
    /// The Orleans-managed persistent state facet.
    /// </param>
    /// <returns>
    /// A keyed <see cref="IStateManager{T}"/> instance.
    /// </returns>
    public static IStateManager<TState> RegisterStateManager<TGrain, TState>(
        this TGrain grain,
        string storageName,
        IPersistentState<TState> storage)
        where TGrain : IGrainBase
        where TState : class, IEquatable<TState>
    {
        ArgumentNullException.ThrowIfNull(grain);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageName);
        ArgumentNullException.ThrowIfNull(storage);

        return RegisterStateManagerCore(
            grain.GrainContext.ActivationServices,
            storageName,
            storage,
            grain.GetType());
    }

    internal static IStateManager<TState> RegisterStateManagerCore<TState>(
        IServiceProvider activationServices,
        string storageName,
        IPersistentState<TState> storage,
        Type grainType)
        where TState : class, IEquatable<TState>
    {
        ArgumentNullException.ThrowIfNull(activationServices);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageName);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(grainType);

        var factory = activationServices.GetKeyedService<IStateManagerFactory<TState>>(storageName);
        if (factory is null)
        {
            throw new InvalidOperationException(
                $"No keyed IStateManagerFactory<{typeof(TState).Name}> registration was found for storage name '{storageName}', " +
                $"state type '{typeof(TState).FullName}', grain type '{grainType.FullName}'. " +
                "Register one via AddDefaultStateManager(...) or AddStateManagerFactory(...).");
        }

        return factory.Create(storage);
    }
}
