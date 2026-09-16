using System.Diagnostics;
using System.Text.Json;
using Orleans.Serialization;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxTraceContextTests : IDisposable
{
    private readonly ActivitySource source = new("test." + Guid.NewGuid().ToString("N"));

    public void Dispose() => source.Dispose();

    [Fact]
    public void Add_captures_the_active_trace_context()
    {
        using var listener = StartListener(ActivitySamplingResult.AllDataAndRecorded);
        using var caller = source.StartActivity("caller");

        var outbox = Outbox<string>.Create().Add("first", DateTimeOffset.UnixEpoch);

        Assert.Equal(caller!.Id, outbox.Envelopes[0].Id.TraceParent);
    }

    [Fact]
    public void Add_without_an_active_activity_captures_no_trace_context()
    {
        var outbox = Outbox<string>.Create().Add("first", DateTimeOffset.UnixEpoch);

        Assert.Null(outbox.Envelopes[0].Id.TraceParent);
    }

    [Fact]
    public void Add_captures_the_trace_context_of_an_unsampled_activity()
    {
        // Decision 2 in the design note: store the traceparent regardless of the
        // sampled flag. Sampling configuration is not stable across the
        // store-and-forward gap, and the trace id stays useful for log correlation
        // even when the producing span is never exported.
        using var listener = StartListener(ActivitySamplingResult.PropagationData);
        using var unsampled = source.StartActivity("unsampled");

        var outbox = Outbox<string>.Create().Add("first", DateTimeOffset.UnixEpoch);

        Assert.False(unsampled!.Recorded);
        Assert.Equal(unsampled.Id, outbox.Envelopes[0].Id.TraceParent);
    }

    [Fact]
    public void AddRange_captures_one_trace_context_for_the_whole_batch()
    {
        using var listener = StartListener(ActivitySamplingResult.AllDataAndRecorded);
        using var caller = source.StartActivity("caller");

        var outbox = Outbox<string>.Create().AddRange(["first", "second"], DateTimeOffset.UnixEpoch);

        Assert.Equal(caller!.Id, outbox.Envelopes[0].Id.TraceParent);
        Assert.Equal(caller.Id, outbox.Envelopes[1].Id.TraceParent);
    }

    [Fact]
    public void Collection_expression_construction_captures_the_active_trace_context()
    {
        using var listener = StartListener(ActivitySamplingResult.AllDataAndRecorded);
        using var caller = source.StartActivity("caller");

        Outbox<string> outbox = ["first", "second"];

        Assert.Equal(caller!.Id, outbox.Envelopes[0].Id.TraceParent);
        Assert.Equal(caller.Id, outbox.Envelopes[1].Id.TraceParent);
    }

    [Fact]
    public void Delivery_token_carries_the_captured_trace_context_to_the_receiver()
    {
        using var listener = StartListener(ActivitySamplingResult.AllDataAndRecorded);
        using var caller = source.StartActivity("caller");
        var outbox = Outbox<string>.Create().Add("first", DateTimeOffset.UnixEpoch);

        var token = outbox.Envelopes[0].Id.ForSender(GrainId.Create("test/sender", "one"));

        Assert.True(token.TryGetTraceParent(out var traceParent));
        Assert.Equal(caller!.Id, traceParent);
    }

    [Fact]
    public void Delivery_token_without_a_captured_trace_context_reports_none()
    {
        var outbox = Outbox<string>.Create().Add("first", DateTimeOffset.UnixEpoch);

        var token = outbox.Envelopes[0].Id.ForSender(GrainId.Create("test/sender", "one"));

        Assert.False(token.TryGetTraceParent(out var traceParent));
        Assert.Null(traceParent);
    }

    [Fact]
    public void Json_round_trip_preserves_the_captured_trace_context()
    {
        using var listener = StartListener(ActivitySamplingResult.AllDataAndRecorded);
        using var caller = source.StartActivity("caller");
        var outbox = Outbox<string>.Create().Add("first", DateTimeOffset.UnixEpoch);

        var restored = JsonSerializer.Deserialize<Outbox<string>>(JsonSerializer.Serialize(outbox));

        Assert.Equal(caller!.Id, restored!.Envelopes[0].Id.TraceParent);
    }

    [Fact]
    public void Json_written_before_trace_capture_still_reads_back()
    {
        // The property is nullable and additive, so snapshots persisted by an
        // earlier version — which carry no TraceParent at all — must still load.
        // This is what makes the change migration-free for stored grain state.
        using var listener = StartListener(ActivitySamplingResult.AllDataAndRecorded);
        using var caller = source.StartActivity("caller");
        var outbox = Outbox<string>.Create().Add("first", DateTimeOffset.UnixEpoch);
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(outbox))!;
        json["Items"]![0]!["Id"]!.AsObject().Remove("TraceParent");

        var restored = JsonSerializer.Deserialize<Outbox<string>>(json.ToJsonString());

        Assert.Null(restored!.Envelopes[0].Id.TraceParent);
        Assert.Equal("first", restored[0]);
    }

    [Fact]
    public void Json_omits_the_property_when_no_trace_context_was_captured()
    {
        // Every pending envelope is rewritten on each WriteStateAsync for as long
        // as the message stays pending, so an absent value must write nothing.
        var outbox = Outbox<string>.Create().Add("first", DateTimeOffset.UnixEpoch);

        var json = JsonSerializer.Serialize(outbox);

        Assert.DoesNotContain("TraceParent", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Orleans_serialization_preserves_the_captured_trace_context()
    {
        using var listener = StartListener(ActivitySamplingResult.AllDataAndRecorded);
        using var caller = source.StartActivity("caller");
        var outbox = Outbox<string>.Create().Add("first", DateTimeOffset.UnixEpoch);
        var services = new ServiceCollection();
        services.AddSerializer(builder => builder.AddAssembly(typeof(Outbox<>).Assembly));
        using var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<Serializer>();

        var loaded = serializer.Deserialize<Outbox<string>>(serializer.SerializeToArray(outbox));

        Assert.Equal(caller!.Id, loaded!.Envelopes[0].Id.TraceParent);
    }

    private ActivityListener StartListener(ActivitySamplingResult samplingResult)
    {
        var started = new ActivityListener
        {
            ShouldListenTo = candidate => candidate.Name == source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => samplingResult
        };

        ActivitySource.AddActivityListener(started);
        return started;
    }
}
