#if NET11_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
#endif

namespace Egil.SystemTextJson.Migration.PerfTests;

// Union dispatch exists only on .NET 11, so the whole scenario is compiled out on net10.0 and
// the shared Setup calls an empty partial method there.
public abstract partial class MigrationScenarioBenchmarksBase
{
    partial void SetupUnionPayloads(PerfPayload? payload);

#if NET11_0_OR_GREATER
    private byte[] plainUnionPayload = null!;
    private byte[] migratableUnionPayload = null!;
    private byte[] migratableUnionMigrationPayload = null!;

    partial void SetupUnionPayloads(PerfPayload? payload)
    {
        // The second case is serialized so that the structural classifier must eliminate the
        // first case by property name, mirroring what the migration classifier does via $type.
        plainUnionPayload = JsonSerializer.SerializeToUtf8Bytes<PerfPlainUnion>(new PerfUnionPlainB("Report", 42, payload), plainOptions);
        migratableUnionPayload = JsonSerializer.SerializeToUtf8Bytes<PerfMigratableUnion>(new PerfUnionMigratableB("Report", 42, payload), migratableStaticOptions);
        migratableUnionMigrationPayload = JsonSerializer.SerializeToUtf8Bytes(new PerfUnionMigratableAV1("Jane Doe", 42, payload), migratableStaticOptions);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Deserialize", "UnionDispatch")]
    public PerfPlainUnion PlainStjUnionDispatchStructural()
        => JsonSerializer.Deserialize<PerfPlainUnion>(plainUnionPayload, plainOptions);

    [Benchmark]
    [BenchmarkCategory("Deserialize", "UnionDispatch")]
    public PerfMigratableUnion JsonMigratableUnionDispatch()
        => JsonSerializer.Deserialize<PerfMigratableUnion>(migratableUnionPayload, migratableStaticOptions);

    [Benchmark]
    [BenchmarkCategory("Deserialize", "UnionDispatch")]
    public PerfMigratableUnion JsonMigratableUnionDispatchWithMigration()
        => JsonSerializer.Deserialize<PerfMigratableUnion>(migratableUnionMigrationPayload, migratableStaticOptions);
#endif
}

#if NET11_0_OR_GREATER
public record class PerfUnionPlainA(
    string Name,
    int Age,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null);

public record class PerfUnionPlainB(
    string Title,
    int Count,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null);

[JsonUnion(TypeClassifier = typeof(JsonUnionTypeStructuralClassifier))]
public union PerfPlainUnion(PerfUnionPlainA, PerfUnionPlainB);

[JsonMigratable(TypeDiscriminator = "union-a-v1")]
public record class PerfUnionMigratableAV1(
    string Name,
    int Age,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null);

[JsonMigratable(TypeDiscriminator = "union-a")]
public record class PerfUnionMigratableA(
    string FirstName,
    string LastName,
    int Age,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null)
    : IMigrateFrom<PerfUnionMigratableAV1, PerfUnionMigratableA>
{
    public static bool TryMigrateFrom(PerfUnionMigratableAV1 source, out PerfUnionMigratableA result)
    {
        (string firstName, string lastName) = PerfNames.Split(source.Name);
        result = new PerfUnionMigratableA(firstName, lastName, source.Age, source.Payload);
        return true;
    }
}

[JsonMigratable(TypeDiscriminator = "union-b")]
public record class PerfUnionMigratableB(
    string Title,
    int Count,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null);

[JsonUnion(TypeClassifier = typeof(JsonMigratableUnionTypeClassifier))]
public union PerfMigratableUnion(PerfUnionMigratableA, PerfUnionMigratableB);
#endif
