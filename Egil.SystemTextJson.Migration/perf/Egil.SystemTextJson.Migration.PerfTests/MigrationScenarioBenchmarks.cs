using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;

namespace Egil.SystemTextJson.Migration.PerfTests;

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Config(typeof(PerfBenchmarkConfig))]
public class ReflectionMigrationScenarioBenchmarks : MigrationScenarioBenchmarksBase
{
    protected override JsonSerializerOptions CreatePlainOptions()
        => new(JsonSerializerDefaults.Web);

    protected override JsonSerializerOptions CreateMigratableNoMigrationOptions()
        => new JsonSerializerOptions(JsonSerializerDefaults.Web).AddJsonMigrationSupport();

    protected override JsonSerializerOptions CreateMigratableStaticOptions()
        => new JsonSerializerOptions(JsonSerializerDefaults.Web).AddJsonMigrationSupport();

    protected override JsonSerializerOptions CreateMigratableExternalOptions()
        => new JsonSerializerOptions(JsonSerializerDefaults.Web)
            .AddJsonMigrationSupport(static builder => builder.RegisterMigrator<PerfExternalMigrator>());
}

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Config(typeof(PerfBenchmarkConfig))]
public class SourceGenMigrationScenarioBenchmarks : MigrationScenarioBenchmarksBase
{
    protected override JsonSerializerOptions CreatePlainOptions()
        => new(PerfJsonContext.Default.Options);

    protected override JsonSerializerOptions CreateMigratableNoMigrationOptions()
    {
        JsonSerializerOptions options = new(PerfJsonContext.Default.Options);
        options.AddJsonMigrationSupport();
        return options;
    }

    protected override JsonSerializerOptions CreateMigratableStaticOptions()
    {
        JsonSerializerOptions options = new(PerfJsonContext.Default.Options);
        options.AddJsonMigrationSupport();
        return options;
    }

    protected override JsonSerializerOptions CreateMigratableExternalOptions()
    {
        JsonSerializerOptions options = new(PerfJsonContext.Default.Options);
        options.AddJsonMigrationSupport(static builder => builder.RegisterMigrator<PerfExternalMigrator>());
        return options;
    }
}

public abstract class MigrationScenarioBenchmarksBase
{
    private JsonSerializerOptions plainOptions = null!;
    private JsonSerializerOptions migratableNoMigrationOptions = null!;
    private JsonSerializerOptions migratableStaticOptions = null!;
    private JsonSerializerOptions migratableExternalOptions = null!;

    private byte[] plainNoMigrationPayload = null!;
    private byte[] polymorphicplainNoMigrationPayload = null!;
    private byte[] migratableNoMigrationPayload = null!;
    private byte[] plainStaticMigrationPayload = null!;
    private byte[] migratableStaticMigrationPayload = null!;
    private byte[] plainExternalMigrationPayload = null!;
    private byte[] migratableExternalMigrationPayload = null!;
    private byte[] plainLegacyPayload = null!;
    private byte[] migratableLegacyPayload = null!;
    private PerfCurrentStatePlain plainCurrentState = null!;
    private PerfExternalPolymorphicPlainV1 polymorphicPlainCurrentState = null!;
    private PerfCurrentStateMigratable migratableCurrentState = null!;

    [Params(2, 32, 256)]
    public int TagCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        plainOptions = CreatePlainOptions();
        migratableNoMigrationOptions = CreateMigratableNoMigrationOptions();
        migratableStaticOptions = CreateMigratableStaticOptions();
        migratableExternalOptions = CreateMigratableExternalOptions();

        string[] tags = CreateTags(TagCount);

        plainCurrentState = new PerfCurrentStatePlain("Egil Hansen", 42, tags);
        polymorphicPlainCurrentState = new PerfExternalPolymorphicPlainV1("Egil Hansen", 42, tags);
        migratableCurrentState = new PerfCurrentStateMigratable("Egil Hansen", 42, tags);

        plainNoMigrationPayload = JsonSerializer.SerializeToUtf8Bytes(plainCurrentState, plainOptions);
        polymorphicplainNoMigrationPayload = JsonSerializer.SerializeToUtf8Bytes(polymorphicPlainCurrentState, plainOptions);

        migratableNoMigrationPayload = JsonSerializer.SerializeToUtf8Bytes(migratableCurrentState, migratableNoMigrationOptions);

        plainStaticMigrationPayload = JsonSerializer.SerializeToUtf8Bytes(new PerfStaticPlainV1("Egil Hansen", 42, tags), plainOptions);
        migratableStaticMigrationPayload = JsonSerializer.SerializeToUtf8Bytes(new PerfStaticV1("Egil Hansen", 42, tags), migratableStaticOptions);

        plainExternalMigrationPayload = JsonSerializer.SerializeToUtf8Bytes(new PerfExternalPlainV1("Egil Hansen", 42, tags), plainOptions);
        migratableExternalMigrationPayload = JsonSerializer.SerializeToUtf8Bytes(new PerfExternalV1("Egil Hansen", 42, tags), migratableExternalOptions);

        plainLegacyPayload = JsonSerializer.SerializeToUtf8Bytes(new PerfExternalPlainV2("Egil", "Hansen", 42, tags), plainOptions);
        migratableLegacyPayload = JsonSerializer.SerializeToUtf8Bytes(new PerfExternalV2("Egil", "Hansen", 42, tags), plainOptions);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Deserialize", "NoMigration")]
    public PerfCurrentStatePlain PlainStjNoMigration()
        => JsonSerializer.Deserialize<PerfCurrentStatePlain>(plainNoMigrationPayload, plainOptions)!;

    [Benchmark]
    [BenchmarkCategory("Deserialize", "NoMigration")]
    public PerfCurrentStatePlain PolymorphicPlainStjNoMigration()
        => JsonSerializer.Deserialize<PerfCurrentStatePlain>(polymorphicplainNoMigrationPayload, plainOptions)!;

    [Benchmark]
    [BenchmarkCategory("Deserialize", "NoMigration")]
    public PerfCurrentStateMigratable JsonMigratableNoMigration()
        => JsonSerializer.Deserialize<PerfCurrentStateMigratable>(migratableNoMigrationPayload, migratableNoMigrationOptions)!;

    [Benchmark]
    [BenchmarkCategory("Deserialize", "StaticMigration")]
    public PerfStaticV2 JsonMigratableStaticMigration()
        => JsonSerializer.Deserialize<PerfStaticV2>(migratableStaticMigrationPayload, migratableStaticOptions)!;

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Deserialize", "StaticMigration")]
    public PerfStaticPlainV2 PlainStjStaticMigrationManual()
    {
        PerfStaticPlainV1 source = JsonSerializer.Deserialize<PerfStaticPlainV1>(plainStaticMigrationPayload, plainOptions)!;
        return PerfStaticPlainV2.ManualFrom(source);
    }

    [Benchmark]
    [BenchmarkCategory("Deserialize", "ExternalMigration")]
    public PerfExternalV2 JsonMigratableExternalMigration()
        => JsonSerializer.Deserialize<PerfExternalV2>(migratableExternalMigrationPayload, migratableExternalOptions)!;

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Deserialize", "ExternalMigration")]
    public PerfExternalPlainV2 PlainStjExternalMigrationManual()
    {
        PerfExternalPlainV1 source = JsonSerializer.Deserialize<PerfExternalPlainV1>(plainExternalMigrationPayload, plainOptions)!;
        return PerfExternalPlainV2.ManualFrom(source);
    }

    [Benchmark]
    [BenchmarkCategory("Deserialize", "LegacyPayload")]
    public PerfExternalV2 JsonMigratableLegacyPayload()
        => JsonSerializer.Deserialize<PerfExternalV2>(migratableLegacyPayload, migratableExternalOptions)!;

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Deserialize", "LegacyPayload")]
    public PerfExternalPlainV2 PlainStjLegacyPayloadManual()
    {
        PerfExternalPlainV2 target = JsonSerializer.Deserialize<PerfExternalPlainV2>(plainLegacyPayload, plainOptions)!;
        // Baseline counterpart keeps migration tracking explicit in user code.
        target.MigratedDuringDeserialization = true;
        return target;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Serialize", "NoMigration")]
    public byte[] PlainStjSerializeNoMigration()
        => JsonSerializer.SerializeToUtf8Bytes(plainCurrentState, plainOptions);

    [Benchmark]
    [BenchmarkCategory("Serialize", "NoMigration")]
    public byte[] PolymorphicPlainStjSerializeNoMigration()
        => JsonSerializer.SerializeToUtf8Bytes(polymorphicPlainCurrentState, plainOptions);

    [Benchmark]
    [BenchmarkCategory("Serialize", "NoMigration")]
    public byte[] JsonMigratableSerializeNoMigration()
        => JsonSerializer.SerializeToUtf8Bytes(migratableCurrentState, migratableNoMigrationOptions);

    protected abstract JsonSerializerOptions CreatePlainOptions();

    protected abstract JsonSerializerOptions CreateMigratableNoMigrationOptions();

    protected abstract JsonSerializerOptions CreateMigratableStaticOptions();

    protected abstract JsonSerializerOptions CreateMigratableExternalOptions();

    private static string[] CreateTags(int count)
    {
        var tags = new string[count];
        for (int index = 0; index < tags.Length; index++)
        {
            tags[index] = $"tag-{index:D3}";
        }

        return tags;
    }
}

public sealed class PerfBenchmarkConfig : ManualConfig
{
    public PerfBenchmarkConfig()
    {
        AddColumnProvider(DefaultColumnProviders.Instance);
        AddJob(Job.Default
            .WithToolchain(InProcessNoEmitToolchain.Instance)
            .WithLaunchCount(1)
            .WithWarmupCount(1)
            .WithIterationCount(5));
    }
}

public record class PerfCurrentStatePlain(string Name, int Age, string[] Tags);

[JsonMigratable]
public record class PerfCurrentStateMigratable(string Name, int Age, string[] Tags);

public record class PerfStaticPlainV1(string Name, int Age, string[] Tags);

public record class PerfStaticPlainV2(string FirstName, string LastName, int Age, string[] Tags)
{
    public static PerfStaticPlainV2 ManualFrom(PerfStaticPlainV1 source)
    {
        var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new PerfStaticPlainV2(
            names.Length > 0 ? names[0] : string.Empty,
            names.Length > 1 ? names[1] : string.Empty,
            source.Age,
            source.Tags);
    }
}

[JsonMigratable]
public record class PerfStaticV1(string Name, int Age, string[] Tags);

[JsonMigratable]
public record class PerfStaticV2(string FirstName, string LastName, int Age, string[] Tags) : IJsonMigrationTracked, IMigrateFrom<PerfStaticV1, PerfStaticV2>
{
    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; set; }

    public static bool TryMigrateFrom(PerfStaticV1 source, out PerfStaticV2 result)
    {
        var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        result = new PerfStaticV2(
            names.Length > 0 ? names[0] : string.Empty,
            names.Length > 1 ? names[1] : string.Empty,
            source.Age,
            source.Tags);
        return true;
    }
}

public record class PerfExternalPlainV1(string Name, int Age, string[] Tags);

public record class PerfExternalPlainV2(string FirstName, string LastName, int Age, string[] Tags)
{
    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; set; }

    public static PerfExternalPlainV2 ManualFrom(PerfExternalPlainV1 source)
    {
        var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new PerfExternalPlainV2(
            names.Length > 0 ? names[0] : string.Empty,
            names.Length > 1 ? names[1] : string.Empty,
            source.Age,
            source.Tags);
    }
}

[JsonPolymorphic]
[JsonDerivedType(typeof(PerfExternalPolymorphicPlainV1), typeDiscriminator: "PerfExternalPolymorphicPlainV1.v1")]
public record class PerfExternalPolymorphicPlainV1(string Name, int Age, string[] Tags);

[JsonMigratable]
public record class PerfExternalV1(string Name, int Age, string[] Tags);

[JsonMigratable]
public record class PerfExternalV2(string FirstName, string LastName, int Age, string[] Tags) : IJsonMigrationTracked
{
    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; set; }
}

public class PerfExternalMigrator : IMigrate<PerfExternalV1, PerfExternalV2>
{
    public bool TryMigrateFrom(PerfExternalV1 source, out PerfExternalV2 result)
    {
        var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        result = new PerfExternalV2(
            names.Length > 0 ? names[0] : string.Empty,
            names.Length > 1 ? names[1] : string.Empty,
            source.Age,
            source.Tags);
        return true;
    }
}

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(PerfCurrentStatePlain))]
[JsonSerializable(typeof(PerfCurrentStateMigratable))]
[JsonSerializable(typeof(PerfStaticPlainV1))]
[JsonSerializable(typeof(PerfStaticPlainV2))]
[JsonSerializable(typeof(PerfStaticV1))]
[JsonSerializable(typeof(PerfStaticV2))]
[JsonSerializable(typeof(PerfExternalPlainV1))]
[JsonSerializable(typeof(PerfExternalPlainV2))]
[JsonSerializable(typeof(PerfExternalV1))]
[JsonSerializable(typeof(PerfExternalV2))]
[JsonSerializable(typeof(PerfExternalPolymorphicPlainV1))]
public partial class PerfJsonContext : JsonSerializerContext;
