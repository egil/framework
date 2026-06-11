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
    private byte[] polymorphicPlainNoMigrationPayload = null!;
    private byte[] migratableNoMigrationPayload = null!;
    private byte[] plainStaticMigrationPayload = null!;
    private byte[] migratableStaticMigrationPayload = null!;
    private byte[] plainExternalMigrationPayload = null!;
    private byte[] migratableExternalMigrationPayload = null!;
    private byte[] plainUndiscriminatedSourceMigrationPayload = null!;
    private byte[] migratableUndiscriminatedSourceMigrationPayload = null!;
    private byte[] plainLegacyPayload = null!;
    private byte[] migratableLegacyPayload = null!;
    private PerfCurrentStatePlain plainCurrentState = null!;
    private PerfPolymorphicPlainBase polymorphicPlainCurrentState = null!;
    private PerfCurrentStateMigratable migratableCurrentState = null!;

    [Params(PayloadSize.Small, PayloadSize.Medium, PayloadSize.Large)]
    public PayloadSize PayloadSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        plainOptions = CreatePlainOptions();
        migratableNoMigrationOptions = CreateMigratableNoMigrationOptions();
        migratableStaticOptions = CreateMigratableStaticOptions();
        migratableExternalOptions = CreateMigratableExternalOptions();

        PerfPayload? payload = PayloadSize == PayloadSize.Small
            ? null
            : PerfPayload.Create(PayloadSize);

        plainCurrentState = new PerfCurrentStatePlain("Jane Doe", 42, payload);
        polymorphicPlainCurrentState = new PerfPolymorphicPlainCurrentState("Jane Doe", 42, payload);
        migratableCurrentState = new PerfCurrentStateMigratable("Jane Doe", 42, payload);

        plainNoMigrationPayload = JsonSerializer.SerializeToUtf8Bytes(plainCurrentState, plainOptions);
        polymorphicPlainNoMigrationPayload = JsonSerializer.SerializeToUtf8Bytes<PerfPolymorphicPlainBase>(polymorphicPlainCurrentState, plainOptions);

        migratableNoMigrationPayload = JsonSerializer.SerializeToUtf8Bytes(migratableCurrentState, migratableNoMigrationOptions);

        plainStaticMigrationPayload = JsonSerializer.SerializeToUtf8Bytes(new PerfStaticPlainV1("Jane Doe", 42, payload), plainOptions);
        migratableStaticMigrationPayload = JsonSerializer.SerializeToUtf8Bytes(new PerfStaticV1("Jane Doe", 42, payload), migratableStaticOptions);

        plainExternalMigrationPayload = JsonSerializer.SerializeToUtf8Bytes(new PerfExternalPlainV1("Jane Doe", 42, payload), plainOptions);
        migratableExternalMigrationPayload = JsonSerializer.SerializeToUtf8Bytes(new PerfExternalV1("Jane Doe", 42, payload), migratableExternalOptions);

        plainUndiscriminatedSourceMigrationPayload = JsonSerializer.SerializeToUtf8Bytes(new PerfUndiscriminatedPlainV1("Jane Doe", 42, payload), plainOptions);
        migratableUndiscriminatedSourceMigrationPayload = JsonSerializer.SerializeToUtf8Bytes(new PerfUndiscriminatedV1("Jane Doe", 42, payload), plainOptions);

        plainLegacyPayload = JsonSerializer.SerializeToUtf8Bytes(new PerfExternalPlainV2("Jane", "Doe", 42, payload), plainOptions);
        migratableLegacyPayload = JsonSerializer.SerializeToUtf8Bytes(new PerfExternalV2("Jane", "Doe", 42, payload), plainOptions);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Deserialize", "NoMigration")]
    public PerfCurrentStatePlain PlainStjNoMigration()
        => JsonSerializer.Deserialize<PerfCurrentStatePlain>(plainNoMigrationPayload, plainOptions)!;

    [Benchmark]
    [BenchmarkCategory("Deserialize", "NoMigration")]
    public PerfPolymorphicPlainBase PolymorphicPlainStjNoMigration()
        => JsonSerializer.Deserialize<PerfPolymorphicPlainBase>(polymorphicPlainNoMigrationPayload, plainOptions)!;

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
    [BenchmarkCategory("Deserialize", "UndiscriminatedSourceMigration")]
    public PerfUndiscriminatedV2 JsonMigratableUndiscriminatedSourceMigration()
        => JsonSerializer.Deserialize<PerfUndiscriminatedV2>(migratableUndiscriminatedSourceMigrationPayload, migratableStaticOptions)!;

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Deserialize", "UndiscriminatedSourceMigration")]
    public PerfUndiscriminatedPlainV2 PlainStjUndiscriminatedSourceMigrationManual()
    {
        PerfUndiscriminatedPlainV1 source = JsonSerializer.Deserialize<PerfUndiscriminatedPlainV1>(plainUndiscriminatedSourceMigrationPayload, plainOptions)!;
        return PerfUndiscriminatedPlainV2.ManualFrom(source);
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
    [BenchmarkCategory("Serialize")]
    public byte[] PlainStjSerialize()
        => JsonSerializer.SerializeToUtf8Bytes(plainCurrentState, plainOptions);

    [Benchmark]
    [BenchmarkCategory("Serialize")]
    public byte[] PolymorphicPlainStjSerialize()
        => JsonSerializer.SerializeToUtf8Bytes<PerfPolymorphicPlainBase>(polymorphicPlainCurrentState, plainOptions);

    [Benchmark]
    [BenchmarkCategory("Serialize")]
    public byte[] JsonMigratableSerialize()
        => JsonSerializer.SerializeToUtf8Bytes(migratableCurrentState, migratableNoMigrationOptions);

    protected abstract JsonSerializerOptions CreatePlainOptions();

    protected abstract JsonSerializerOptions CreateMigratableNoMigrationOptions();

    protected abstract JsonSerializerOptions CreateMigratableStaticOptions();

    protected abstract JsonSerializerOptions CreateMigratableExternalOptions();
}

public enum PayloadSize
{
    Small,
    Medium,
    Large,
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

public record class PerfCurrentStatePlain(
    string Name,
    int Age,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PerfPolymorphicPlainCurrentState), typeDiscriminator: "PerfCurrentState.v1")]
public abstract record class PerfPolymorphicPlainBase;

public record class PerfPolymorphicPlainCurrentState(
    string Name,
    int Age,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null) : PerfPolymorphicPlainBase;

[JsonMigratable]
public record class PerfCurrentStateMigratable(
    string Name,
    int Age,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null);

public record class PerfStaticPlainV1(
    string Name,
    int Age,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null);

public record class PerfStaticPlainV2(
    string FirstName,
    string LastName,
    int Age,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null)
{
    public static PerfStaticPlainV2 ManualFrom(PerfStaticPlainV1 source)
    {
        (string firstName, string lastName) = PerfNames.Split(source.Name);
        return new PerfStaticPlainV2(firstName, lastName, source.Age, source.Payload);
    }
}

[JsonMigratable]
public record class PerfStaticV1(
    string Name,
    int Age,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null);

[JsonMigratable]
public record class PerfStaticV2(
    string FirstName,
    string LastName,
    int Age,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null) : IJsonMigrationTracked, IMigrateFrom<PerfStaticV1, PerfStaticV2>
{
    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; set; }

    public static bool TryMigrateFrom(PerfStaticV1 source, out PerfStaticV2 result)
    {
        (string firstName, string lastName) = PerfNames.Split(source.Name);
        result = new PerfStaticV2(firstName, lastName, source.Age, source.Payload);
        return true;
    }
}

public record class PerfExternalPlainV1(
    string Name,
    int Age,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null);

public record class PerfExternalPlainV2(
    string FirstName,
    string LastName,
    int Age,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null)
{
    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; set; }

    public static PerfExternalPlainV2 ManualFrom(PerfExternalPlainV1 source)
    {
        (string firstName, string lastName) = PerfNames.Split(source.Name);
        return new PerfExternalPlainV2(firstName, lastName, source.Age, source.Payload);
    }
}

[JsonMigratable]
public record class PerfExternalV1(
    string Name,
    int Age,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null);

[JsonMigratable]
public record class PerfExternalV2(
    string FirstName,
    string LastName,
    int Age,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null) : IJsonMigrationTracked
{
    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; set; }
}

public class PerfExternalMigrator : IMigrate<PerfExternalV1, PerfExternalV2>
{
    public bool TryMigrateFrom(PerfExternalV1 source, out PerfExternalV2 result)
    {
        (string firstName, string lastName) = PerfNames.Split(source.Name);
        result = new PerfExternalV2(firstName, lastName, source.Age, source.Payload);
        return true;
    }
}

public record class PerfUndiscriminatedPlainV1(
    string Name,
    int Age,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null);

public record class PerfUndiscriminatedPlainV2(
    string FirstName,
    string LastName,
    int Age,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null)
{
    public static PerfUndiscriminatedPlainV2 ManualFrom(PerfUndiscriminatedPlainV1 source)
    {
        (string firstName, string lastName) = PerfNames.Split(source.Name);
        return new PerfUndiscriminatedPlainV2(firstName, lastName, source.Age, source.Payload);
    }
}

public record class PerfUndiscriminatedV1(
    string Name,
    int Age,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null);

[JsonMigratable(UndiscriminatedSourceType = typeof(PerfUndiscriminatedV1))]
public record class PerfUndiscriminatedV2(
    string FirstName,
    string LastName,
    int Age,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfPayload? Payload = null) :
    IJsonMigrationTracked,
    IMigrateFrom<PerfUndiscriminatedV1, PerfUndiscriminatedV2>
{
    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; set; }

    public static bool TryMigrateFrom(PerfUndiscriminatedV1 source, out PerfUndiscriminatedV2 result)
    {
        (string firstName, string lastName) = PerfNames.Split(source.Name);
        result = new PerfUndiscriminatedV2(firstName, lastName, source.Age, source.Payload);
        return true;
    }
}

public record class PerfPayload(
    string Status,
    bool IsActive,
    decimal Balance,
    DateTimeOffset UpdatedAt,
    PerfContact Contact,
    string[] Labels,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfLineItem[]? Items = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PerfHistoryEntry[]? History = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Dictionary<string, string>? Attributes = null)
{
    public static PerfPayload Create(PayloadSize size)
    {
        PerfPayloadProfile profile = PerfPayloadProfile.For(size);

        return new PerfPayload(
            Status: "Processing",
            IsActive: true,
            Balance: 1200.50m + profile.ItemCount,
            UpdatedAt: new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero).AddDays(profile.HistoryCount),
            Contact: new PerfContact(
                "jane.doe@example.net",
                "+45 12 34 56 78",
                CreateText("Contact note", profile.TextRepeat)),
            Labels: CreateLabels(profile.LabelCount),
            Items: profile.ItemCount > 0 ? CreateItems(profile.ItemCount, profile.TextRepeat) : null,
            History: profile.HistoryCount > 0 ? CreateHistory(profile.HistoryCount, profile.TextRepeat) : null,
            Attributes: profile.AttributeCount > 0 ? CreateAttributes(profile.AttributeCount, profile.TextRepeat) : null);
    }

    private static string[] CreateLabels(int count)
    {
        var labels = new string[count];
        for (int index = 0; index < labels.Length; index++)
        {
            labels[index] = $"label-{index:D3}";
        }

        return labels;
    }

    private static PerfLineItem[] CreateItems(int count, int textRepeat)
    {
        var items = new PerfLineItem[count];
        for (int index = 0; index < items.Length; index++)
        {
            items[index] = new PerfLineItem(
                $"SKU-{index:D5}",
                CreateText($"Line item {index:D3}", textRepeat),
                Quantity: (index % 7) + 1,
                UnitPrice: 9.95m + index,
                DiscountRate: index % 3 == 0 ? 0.15d : 0.05d,
                IsBackordered: index % 11 == 0,
                Dimensions: new PerfLineDimensions(
                    WeightKg: 0.5m + index,
                    LengthCm: 10 + index,
                    WidthCm: 5 + (index % 9),
                    HeightCm: 3 + (index % 5)));
        }

        return items;
    }

    private static PerfHistoryEntry[] CreateHistory(int count, int textRepeat)
    {
        var history = new PerfHistoryEntry[count];
        var start = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        for (int index = 0; index < history.Length; index++)
        {
            history[index] = new PerfHistoryEntry(
                start.AddHours(index),
                $"user-{index % 8:D2}",
                index % 2 == 0 ? "Updated" : "Reviewed",
                CreateText($"History note {index:D3}", textRepeat),
                Attempt: index + 1,
                Success: index % 5 != 0);
        }

        return history;
    }

    private static Dictionary<string, string> CreateAttributes(int count, int textRepeat)
    {
        var attributes = new Dictionary<string, string>(count, StringComparer.Ordinal);
        for (int index = 0; index < count; index++)
        {
            attributes[$"attribute-{index:D3}"] = CreateText($"Attribute value {index:D3}", textRepeat);
        }

        return attributes;
    }

    private static string CreateText(string prefix, int repetitions)
    {
        var parts = new string[repetitions];
        for (int index = 0; index < parts.Length; index++)
        {
            parts[index] = $"{prefix} segment {index:D2} contains representative JSON text.";
        }

        return string.Join(' ', parts);
    }
}

public record class PerfContact(
    string Email,
    string Phone,
    string Notes);

public record class PerfLineItem(
    string Sku,
    string Description,
    int Quantity,
    decimal UnitPrice,
    double DiscountRate,
    bool IsBackordered,
    PerfLineDimensions Dimensions);

public record class PerfLineDimensions(
    decimal WeightKg,
    decimal LengthCm,
    decimal WidthCm,
    decimal HeightCm);

public record class PerfHistoryEntry(
    DateTimeOffset OccurredAt,
    string Actor,
    string Action,
    string Notes,
    int Attempt,
    bool Success);

internal sealed record PerfPayloadProfile(
    int LabelCount,
    int ItemCount,
    int HistoryCount,
    int AttributeCount,
    int TextRepeat)
{
    public static PerfPayloadProfile For(PayloadSize size)
        => size switch
        {
            PayloadSize.Small => new PerfPayloadProfile(0, 0, 0, 0, 0),
            PayloadSize.Medium => new PerfPayloadProfile(3, 0, 0, 0, 2),
            PayloadSize.Large => new PerfPayloadProfile(8, 5, 3, 8, 8),
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
        };
}

internal static class PerfNames
{
    public static (string FirstName, string LastName) Split(string name)
    {
        var names = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (
            names.Length > 0 ? names[0] : string.Empty,
            names.Length > 1 ? names[1] : string.Empty);
    }
}

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(PerfCurrentStatePlain))]
[JsonSerializable(typeof(PerfPolymorphicPlainBase))]
[JsonSerializable(typeof(PerfPolymorphicPlainCurrentState))]
[JsonSerializable(typeof(PerfCurrentStateMigratable))]
[JsonSerializable(typeof(PerfStaticPlainV1))]
[JsonSerializable(typeof(PerfStaticPlainV2))]
[JsonSerializable(typeof(PerfStaticV1))]
[JsonSerializable(typeof(PerfStaticV2))]
[JsonSerializable(typeof(PerfExternalPlainV1))]
[JsonSerializable(typeof(PerfExternalPlainV2))]
[JsonSerializable(typeof(PerfExternalV1))]
[JsonSerializable(typeof(PerfExternalV2))]
[JsonSerializable(typeof(PerfUndiscriminatedPlainV1))]
[JsonSerializable(typeof(PerfUndiscriminatedPlainV2))]
[JsonSerializable(typeof(PerfUndiscriminatedV1))]
[JsonSerializable(typeof(PerfUndiscriminatedV2))]
[JsonSerializable(typeof(PerfPayload))]
[JsonSerializable(typeof(PerfContact))]
[JsonSerializable(typeof(PerfLineItem))]
[JsonSerializable(typeof(PerfLineDimensions))]
[JsonSerializable(typeof(PerfHistoryEntry))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(PerfLineItem[]))]
[JsonSerializable(typeof(PerfHistoryEntry[]))]
public partial class PerfJsonContext : JsonSerializerContext;
