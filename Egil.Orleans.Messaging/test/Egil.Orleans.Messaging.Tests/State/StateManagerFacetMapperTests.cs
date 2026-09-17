using System.Reflection;

namespace Egil.Orleans.Messaging.Tests.State;

public sealed class StateManagerFacetMapperTests
{
    [Fact]
    public void Mapper_rejects_a_state_type_that_cannot_represent_an_absent_record()
    {
        var mapper = new StateManagerFacetMapper(new PersistentStateAttributeMapper());
        var parameter = ParameterOf<UndefaultableStateGrain>();

        var ex = Assert.Throws<ArgumentException>(() =>
            mapper.GetFactory(parameter, new PersistentStateAttribute("state", "Default")));

        Assert.Equal("state", ex.ParamName);
        Assert.Contains(typeof(UndefaultableState).FullName!, ex.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(UndefaultableStateGrain).FullName!, ex.Message, StringComparison.Ordinal);
        Assert.Contains("IStateDefault<UndefaultableState>", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mapper_hands_a_raw_persistent_state_parameter_back_to_the_mapper_it_decorates()
    {
        var inner = new RecordingAttributeMapper();
        var mapper = new StateManagerFacetMapper(inner);
        var parameter = ParameterOf<RawFacetGrain>();

        mapper.GetFactory(parameter, new PersistentStateAttribute("state", "Default"));

        Assert.Same(parameter, inner.LastParameter);
    }

    [Fact]
    public void State_contract_detection_does_not_throw_for_a_type_that_does_not_implement_it()
    {
        // IStateDefault<TSelf> constrains its own argument, so asking the question with
        // MakeGenericType throws for exactly the types the answer is "no" for.
        Assert.False(StateContract.ImplementsStateDefault(typeof(UndefaultableState)));
        Assert.True(StateContract.ImplementsStateDefault(typeof(ContractState)));
    }

    [Fact]
    public void State_contract_reports_no_instance_factory_without_a_parameterless_constructor()
    {
        Assert.Null(StateContract<UndefaultableState>.CreateInstance);
        Assert.NotNull(StateContract<FacetState>.CreateInstance);
    }

    private static ParameterInfo ParameterOf<TGrain>() =>
        typeof(TGrain).GetConstructors().Single().GetParameters().Single();

    [GenerateSerializer]
    public sealed record UndefaultableState([property: Id(0)] string Value);

    public sealed class UndefaultableStateGrain(
        [PersistentState("state", "Default")] IStateManager<UndefaultableState> state)
    {
        public IStateManager<UndefaultableState> State { get; } = state;
    }

    public sealed class RawFacetGrain(
        [PersistentState("state", "Default")] IPersistentState<FacetState> storage)
    {
        public IPersistentState<FacetState> Storage { get; } = storage;
    }

    private sealed class RecordingAttributeMapper : IAttributeToFactoryMapper<PersistentStateAttribute>
    {
        public ParameterInfo? LastParameter { get; private set; }

        public Factory<IGrainContext, object> GetFactory(ParameterInfo parameter, PersistentStateAttribute metadata)
        {
            LastParameter = parameter;
            return static _ => new object();
        }
    }
}
