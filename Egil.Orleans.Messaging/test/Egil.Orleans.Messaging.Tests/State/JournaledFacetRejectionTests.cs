using Orleans.Journaling;

namespace Egil.Orleans.Messaging.Tests.State;

/// <summary>
/// Orleans.Journaling registers its own <see cref="IPersistentState{TState}"/> for
/// <c>KeyedService.AnyKey</c>, so a journaled facet can reach
/// <c>RegisterStateManager</c> by accident. It must not be wrapped.
/// </summary>
public sealed class JournaledFacetRejectionTests
{
    [Fact]
    public void A_journaled_facet_is_refused()
    {
        var storage = new FakeJournaledState(new TestState("stored"));

        var error = Assert.Throws<NotSupportedException>(
            () => new DefaultStateManager<TestState>(storage, static () => new("default")));

        // Its ReadStateAsync is a no-op, so recovery would compare the attempted value
        // with itself and report a failed write as a successful one.
        Assert.Contains("journaled state", error.Message, StringComparison.Ordinal);
        Assert.Contains("failed write as a successful one", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ordinary_facet_is_accepted()
    {
        var storage = new FakeStorage(new TestState("stored"));

        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));

        Assert.Equal(new TestState("stored"), manager.State);
    }

    private sealed record TestState(string Value);

    private class FakeStorage(TestState state) : IPersistentState<TestState>
    {
        public string Etag { get; set; } = "etag-1";
        public bool RecordExists => true;
        public TestState State { get; set; } = state;
        public Task ReadStateAsync() => Task.CompletedTask;
        public Task WriteStateAsync() => Task.CompletedTask;
        public Task ClearStateAsync() => Task.CompletedTask;
        public Task ReadStateAsync(CancellationToken cancellationToken) => ReadStateAsync();
        public Task WriteStateAsync(CancellationToken cancellationToken) => WriteStateAsync();
        public Task ClearStateAsync(CancellationToken cancellationToken) => ClearStateAsync();
    }

    // Carries the real marker. Only the interface's identity matters to the detection —
    // the journaling members are never reached, because construction is refused first.
#pragma warning disable ORLEANSEXP005 // Journaling is experimental; this only names its marker.
    private sealed class FakeJournaledState(TestState state) : FakeStorage(state), IJournaledState
    {
        public void ReplayEntry(JournalEntry entry, JournalReplayContext context) => throw new NotSupportedException();
        public void Reset(JournalStreamWriter writer) => throw new NotSupportedException();
        public void AppendEntries(JournalStreamWriter writer) => throw new NotSupportedException();
        public void AppendSnapshot(JournalStreamWriter writer) => throw new NotSupportedException();
        public IJournaledState DeepCopy() => throw new NotSupportedException();
    }
#pragma warning restore ORLEANSEXP005
}
