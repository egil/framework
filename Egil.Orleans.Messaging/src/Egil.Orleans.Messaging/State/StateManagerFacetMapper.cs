using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Egil.Orleans.Messaging.State;

/// <summary>
/// Maps <c>[PersistentState]</c> to an <see cref="IStateManager{T}"/> constructor
/// parameter, and delegates every other parameter shape to the mapper it decorates.
/// </summary>
/// <remarks>
/// <para>
/// Orleans resolves a facet attribute to a constructor argument through
/// <see cref="IAttributeToFactoryMapper{TMetadata}"/>, looked up from DI by the
/// attribute's own type. Its built-in mapper for <see cref="PersistentStateAttribute"/>
/// rejects any parameter that is not <see cref="IPersistentState{TState}"/>, so binding
/// a manager means taking that registration over. Decorating rather than replacing keeps
/// grains that inject the raw facet working, and lets a second decorator compose.
/// </para>
/// <para>
/// Reusing Orleans' own attribute is what makes this possible at all: the mapper is
/// handed the attribute, which carries both the state name and the storage provider
/// name. A keyed DI registration carries one string and would have to encode two.
/// </para>
/// </remarks>
internal sealed class StateManagerFacetMapper(IAttributeToFactoryMapper<PersistentStateAttribute> inner)
    : IAttributeToFactoryMapper<PersistentStateAttribute>
{
    private static readonly MethodInfo CreateManagerMethod =
        typeof(StateManagerFacetMapper).GetMethod(nameof(CreateManager), BindingFlags.NonPublic | BindingFlags.Static)!;

    public Factory<IGrainContext, object> GetFactory(ParameterInfo parameter, PersistentStateAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(attribute);

        var parameterType = parameter.ParameterType;
        if (!parameterType.IsGenericType || parameterType.GetGenericTypeDefinition() != typeof(IStateManager<>))
        {
            return inner.GetFactory(parameter, attribute);
        }

        var stateType = parameterType.GetGenericArguments()[0];
        if (!CanCreateDefault(stateType))
        {
            throw new ArgumentException(
                $"State type '{stateType.FullName}' on the constructor for '{parameter.Member.DeclaringType?.FullName}' " +
                $"has no public parameterless constructor, so an absent storage record cannot be represented. " +
                $"Implement IStateDefault<{stateType.Name}> on it, or inject IPersistentState<{stateType.Name}> " +
                "and call RegisterStateManager with a state factory.",
                parameter.Name);
        }

        // Orleans falls back to the parameter name when the attribute omits the state name;
        // mirror that so a manager and a raw facet name their record the same way.
        var stateName = string.IsNullOrEmpty(attribute.StateName) ? parameter.Name! : attribute.StateName;

        // Orleans reads a blank storage name as "the default provider" when it builds the
        // facet, so the manager lookup has to agree. Forwarding the raw value would send the
        // facet to default storage and then hunt for a keyed factory registered under an
        // empty string.
        var storageName = string.IsNullOrWhiteSpace(attribute.StorageName) ? null : attribute.StorageName;
        var configuration = new FacetConfiguration(stateName, storageName);
        var grainType = parameter.Member.DeclaringType!;
        var createManager = CreateManagerMethod.MakeGenericMethod(stateType);

        return context => createManager.Invoke(null, [context, configuration, storageName, grainType])!;
    }

    private static bool CanCreateDefault(Type stateType)
        => StateContract.ImplementsStateDefault(stateType)
           || StateContract.HasParameterlessConstructor(stateType);

    private static object CreateManager<T>(
        IGrainContext context,
        IPersistentStateConfiguration configuration,
        string? storageName,
        Type grainType)
        where T : class, IEquatable<T>
    {
        // Building the facet through Orleans' own factory is what makes the injected manager
        // a peer of the raw facet rather than an imitation of it: the state it returns
        // subscribes itself to activation hydration and to the migration handoff.
        var storage = context.ActivationServices
            .GetRequiredService<IPersistentStateFactory>()
            .Create<T>(context, configuration);

        // Facet arguments are built to call the grain constructor, so the grain does not exist
        // yet and the manager must be the lifecycle-deferred kind.
        return StateManagerExtensions.RegisterStateManagerCore(
            context,
            storageName,
            storage,
            grainType,
            createInitialState: null,
            configureState: null,
            lifecycle: context.ObservableLifecycle);
    }

    private sealed record FacetConfiguration(string StateName, string? StorageName) : IPersistentStateConfiguration;
}
