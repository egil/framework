namespace Egil.Orleans.Testing.Tests;

public class CoreTypesTests
{
    [Fact]
    public void GrainActivity_stores_constructor_values()
    {
        var grainId = GrainId.Create("test-grain", "alpha");
        var timestamp = new DateTimeOffset(2026, 4, 29, 12, 30, 0, TimeSpan.Zero);

        var activity = new GrainActivity(grainId, GrainActivityKind.StorageWrite, timestamp);

        Assert.Equal(grainId, activity.GrainId);
        Assert.Equal(GrainActivityKind.StorageWrite, activity.Kind);
        Assert.Equal(timestamp, activity.Timestamp);
    }

    [Fact]
    public void StorageOperation_stores_constructor_values()
    {
        var grainId = GrainId.Create("test-grain", "beta");
        var state = new object();

        var operation = new StorageOperation(
            StorageOperationKind.Write,
            grainId,
            "memory",
            "state-name",
            "etag-42",
            state);

        Assert.Equal(StorageOperationKind.Write, operation.Kind);
        Assert.Equal(grainId, operation.GrainId);
        Assert.Equal("memory", operation.StorageName);
        Assert.Equal("state-name", operation.StateName);
        Assert.Equal("etag-42", operation.Etag);
        Assert.Same(state, operation.State);
    }

    [Fact]
    public void WaitForAssertionDefaults_timeout_defaults_to_five_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), WaitForAssertionDefaults.Timeout);
    }
}
