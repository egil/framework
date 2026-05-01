namespace Egil.Orleans.Testing.Tests;

public interface ITestStateGrain : IGrainWithStringKey
{
    ValueTask SetValueAsync(string value);

    ValueTask<string?> GetValueAsync();

    ValueTask<int> IncrementAsync();

    ValueTask<int> GetNumberAsync();
}

public sealed class TestState
{
    public string? Value { get; set; }

    public int Number { get; set; }
}

public sealed class TestStateGrain([PersistentState("state", "Default")] IPersistentState<TestState> state)
    : Grain, ITestStateGrain
{
    public async ValueTask SetValueAsync(string value)
    {
        state.State.Value = value;
        await state.WriteStateAsync();
    }

    public ValueTask<string?> GetValueAsync() => ValueTask.FromResult(state.State.Value);

    public async ValueTask<int> IncrementAsync()
    {
        state.State.Number++;
        await state.WriteStateAsync();
        return state.State.Number;
    }

    public ValueTask<int> GetNumberAsync() => ValueTask.FromResult(state.State.Number);
}
