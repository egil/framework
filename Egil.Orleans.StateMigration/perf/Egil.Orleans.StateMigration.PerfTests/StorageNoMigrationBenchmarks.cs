using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using Egil.Orleans.StateMigration.SystemTextJson;

namespace Egil.Orleans.StateMigration.PerfTests;

public abstract class StorageNoMigrationBenchmarksBase<TState>
    where TState : class
{
    private JsonSerializerOptions _plainReflectionOptions = null!;
    private JsonSerializerOptions _storageReflectionOptions = null!;
    private JsonSerializerOptions _storageSourceGenStateOnlyOptions = null!;
    private JsonSerializerOptions _storageSourceGenClosedTypeOptions = null!;
    private TState _state = null!;
    private Storage<TState> _storageState = null!;
    private byte[] _plainJsonUtf8 = null!;
    private byte[] _storageJsonUtf8 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _plainReflectionOptions = new JsonSerializerOptions();
        _storageReflectionOptions = new JsonSerializerOptions().AddStateMigrationSupport();
        _storageSourceGenStateOnlyOptions = CreateStorageSourceGenStateOnlyOptions();
        _storageSourceGenClosedTypeOptions = CreateStorageSourceGenClosedTypeOptions();

        _state = CreateState();
        _storageState = new Storage<TState> { Value = _state };

        _plainJsonUtf8 = JsonSerializer.SerializeToUtf8Bytes(_state, _plainReflectionOptions);
        _storageJsonUtf8 = JsonSerializer.SerializeToUtf8Bytes(_storageState, _storageReflectionOptions);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Deserialize", "Reflection")]
    public TState PlainStjDeserializeReflection()
        => JsonSerializer.Deserialize<TState>(_plainJsonUtf8, _plainReflectionOptions)!;

    [Benchmark]
    [BenchmarkCategory("Deserialize", "Reflection")]
    public Storage<TState> StateMigrationDeserializeNoMigrationReflection()
        => JsonSerializer.Deserialize<Storage<TState>>(_storageJsonUtf8, _storageReflectionOptions)!;

    [Benchmark]
    [BenchmarkCategory("Deserialize", "SourceGen")]
    public TState PlainStjDeserializeSourceGen()
        => DeserializePlainSourceGen(_plainJsonUtf8);

    [Benchmark]
    [BenchmarkCategory("Deserialize", "SourceGen")]
    public Storage<TState> StateMigrationDeserializeNoMigrationSourceGenStateOnlyContext()
        => JsonSerializer.Deserialize<Storage<TState>>(_storageJsonUtf8, _storageSourceGenStateOnlyOptions)!;

    [Benchmark]
    [BenchmarkCategory("Deserialize", "SourceGen")]
    public Storage<TState> StateMigrationDeserializeNoMigrationSourceGenClosedStorageContext()
        => JsonSerializer.Deserialize<Storage<TState>>(_storageJsonUtf8, _storageSourceGenClosedTypeOptions)!;

    [Benchmark]
    [BenchmarkCategory("Serialize", "Reflection")]
    public byte[] PlainStjSerializeReflection()
        => JsonSerializer.SerializeToUtf8Bytes(_state, _plainReflectionOptions);

    [Benchmark]
    [BenchmarkCategory("Serialize", "Reflection")]
    public byte[] StateMigrationSerializeReflection()
        => JsonSerializer.SerializeToUtf8Bytes(_storageState, _storageReflectionOptions);

    [Benchmark]
    [BenchmarkCategory("Serialize", "SourceGen")]
    public byte[] PlainStjSerializeSourceGen()
        => SerializePlainSourceGen(_state);

    [Benchmark]
    [BenchmarkCategory("Serialize", "SourceGen")]
    public byte[] StateMigrationSerializeSourceGenStateOnlyContext()
        => JsonSerializer.SerializeToUtf8Bytes(_storageState, _storageSourceGenStateOnlyOptions);

    [Benchmark]
    [BenchmarkCategory("Serialize", "SourceGen")]
    public byte[] StateMigrationSerializeSourceGenClosedStorageContext()
        => JsonSerializer.SerializeToUtf8Bytes(_storageState, _storageSourceGenClosedTypeOptions);

    protected abstract TState CreateState();

    protected abstract byte[] SerializePlainSourceGen(TState state);

    protected abstract TState DeserializePlainSourceGen(byte[] utf8Json);

    protected abstract JsonSerializerOptions CreateStorageSourceGenStateOnlyOptions();

    protected abstract JsonSerializerOptions CreateStorageSourceGenClosedTypeOptions();
}

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Config(typeof(PerfBenchmarkConfig))]
public class MinimalStateNoMigrationBenchmarks : StorageNoMigrationBenchmarksBase<MinimalState>
{
    protected override MinimalState CreateState()
        => new() { DisplayName = "alice" };

    protected override byte[] SerializePlainSourceGen(MinimalState state)
        => JsonSerializer.SerializeToUtf8Bytes(state, MinimalStateJsonContext.Default.MinimalState);

    protected override MinimalState DeserializePlainSourceGen(byte[] utf8Json)
        => JsonSerializer.Deserialize(utf8Json, MinimalStateJsonContext.Default.MinimalState)!;

    protected override JsonSerializerOptions CreateStorageSourceGenStateOnlyOptions()
        => new JsonSerializerOptions(MinimalStateOnlyStorageJsonContext.Default.Options)
            .AddStateMigrationSupport();

    protected override JsonSerializerOptions CreateStorageSourceGenClosedTypeOptions()
        => new JsonSerializerOptions(MinimalStateClosedStorageJsonContext.Default.Options)
            .AddStateMigrationSupport();
}

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Config(typeof(PerfBenchmarkConfig))]
public class ComplexStateNoMigrationBenchmarks : StorageNoMigrationBenchmarksBase<ComplexState>
{
    protected override ComplexState CreateState()
    {
        List<ComplexLineItem> items =
        [
            new ComplexLineItem { Sku = "A-100", Quantity = 2, UnitPrice = 12.5m },
            new ComplexLineItem { Sku = "B-201", Quantity = 4, UnitPrice = 9.99m },
            new ComplexLineItem { Sku = "C-876", Quantity = 1, UnitPrice = 105.25m },
            new ComplexLineItem { Sku = "D-007", Quantity = 8, UnitPrice = 1.75m },
            new ComplexLineItem { Sku = "E-333", Quantity = 3, UnitPrice = 18.40m },
            new ComplexLineItem { Sku = "F-512", Quantity = 6, UnitPrice = 7.15m },
        ];

        Dictionary<string, int> counters = new(StringComparer.Ordinal)
        {
            ["emailsSent"] = 17,
            ["discountsApplied"] = 3,
            ["itemsRepriced"] = 11,
            ["idempotencyHits"] = 2,
        };

        return new ComplexState
        {
            TenantId = "tenant-alpha",
            AggregateId = "cart-000123",
            Revision = 42,
            UpdatedUtc = new DateTimeOffset(2026, 2, 25, 12, 0, 0, TimeSpan.Zero),
            BillingAddress = new ComplexAddress
            {
                Street = "Main Street 1",
                City = "Copenhagen",
                PostalCode = "2100",
                CountryCode = "DK",
            },
            ShippingAddress = new ComplexAddress
            {
                Street = "Market Street 40",
                City = "Aarhus",
                PostalCode = "8000",
                CountryCode = "DK",
            },
            Items = items,
            Counters = counters,
            Tags = ["vip", "priority", "newsletter"],
        };
    }

    protected override byte[] SerializePlainSourceGen(ComplexState state)
        => JsonSerializer.SerializeToUtf8Bytes(state, ComplexStateJsonContext.Default.ComplexState);

    protected override ComplexState DeserializePlainSourceGen(byte[] utf8Json)
        => JsonSerializer.Deserialize(utf8Json, ComplexStateJsonContext.Default.ComplexState)!;

    protected override JsonSerializerOptions CreateStorageSourceGenStateOnlyOptions()
        => new JsonSerializerOptions(ComplexStateOnlyStorageJsonContext.Default.Options)
            .AddStateMigrationSupport();

    protected override JsonSerializerOptions CreateStorageSourceGenClosedTypeOptions()
        => new JsonSerializerOptions(ComplexStateClosedStorageJsonContext.Default.Options)
            .AddStateMigrationSupport();
}

public abstract class StoragePayloadLayoutBenchmarksBase<TState>
    where TState : class
{
    private JsonSerializerOptions _envelopedReflectionOptions = null!;
    private JsonSerializerOptions _flattenedReflectionOptions = null!;
    private JsonSerializerOptions _envelopedSourceGenStateOnlyOptions = null!;
    private JsonSerializerOptions _flattenedSourceGenStateOnlyOptions = null!;
    private JsonSerializerOptions _envelopedSourceGenClosedTypeOptions = null!;
    private JsonSerializerOptions _flattenedSourceGenClosedTypeOptions = null!;
    private Storage<TState> _storageState = null!;
    private byte[] _envelopedJsonUtf8 = null!;
    private byte[] _flattenedJsonUtf8 = null!;

    [Params(StoragePayloadLayout.Enveloped, StoragePayloadLayout.Flattened)]
    public StoragePayloadLayout PayloadLayout { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _storageState = new Storage<TState> { Value = CreateState() };

        _envelopedReflectionOptions = new JsonSerializerOptions()
            .AddStateMigrationSupport(payloadLayout: StoragePayloadLayout.Enveloped);
        _flattenedReflectionOptions = new JsonSerializerOptions()
            .AddStateMigrationSupport(payloadLayout: StoragePayloadLayout.Flattened);
        _envelopedSourceGenStateOnlyOptions =
            CreateStorageSourceGenStateOnlyOptions(StoragePayloadLayout.Enveloped);
        _flattenedSourceGenStateOnlyOptions =
            CreateStorageSourceGenStateOnlyOptions(StoragePayloadLayout.Flattened);
        _envelopedSourceGenClosedTypeOptions =
            CreateStorageSourceGenClosedTypeOptions(StoragePayloadLayout.Enveloped);
        _flattenedSourceGenClosedTypeOptions =
            CreateStorageSourceGenClosedTypeOptions(StoragePayloadLayout.Flattened);

        _envelopedJsonUtf8 = JsonSerializer.SerializeToUtf8Bytes(_storageState, _envelopedReflectionOptions);
        _flattenedJsonUtf8 = JsonSerializer.SerializeToUtf8Bytes(_storageState, _flattenedReflectionOptions);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Layout", "Deserialize", "Reflection")]
    public Storage<TState> StateMigrationDeserializeLayoutReflection()
        => JsonSerializer.Deserialize<Storage<TState>>(GetLayoutJson(), GetReflectionOptions())!;

    [Benchmark]
    [BenchmarkCategory("Layout", "Deserialize", "SourceGen")]
    public Storage<TState> StateMigrationDeserializeLayoutSourceGenStateOnlyContext()
        => JsonSerializer.Deserialize<Storage<TState>>(GetLayoutJson(), GetSourceGenStateOnlyOptions())!;

    [Benchmark]
    [BenchmarkCategory("Layout", "Deserialize", "SourceGen")]
    public Storage<TState> StateMigrationDeserializeLayoutSourceGenClosedStorageContext()
        => JsonSerializer.Deserialize<Storage<TState>>(GetLayoutJson(), GetSourceGenClosedTypeOptions())!;

    [Benchmark]
    [BenchmarkCategory("Layout", "Serialize", "Reflection")]
    public byte[] StateMigrationSerializeLayoutReflection()
        => JsonSerializer.SerializeToUtf8Bytes(_storageState, GetReflectionOptions());

    [Benchmark]
    [BenchmarkCategory("Layout", "Serialize", "SourceGen")]
    public byte[] StateMigrationSerializeLayoutSourceGenStateOnlyContext()
        => JsonSerializer.SerializeToUtf8Bytes(_storageState, GetSourceGenStateOnlyOptions());

    [Benchmark]
    [BenchmarkCategory("Layout", "Serialize", "SourceGen")]
    public byte[] StateMigrationSerializeLayoutSourceGenClosedStorageContext()
        => JsonSerializer.SerializeToUtf8Bytes(_storageState, GetSourceGenClosedTypeOptions());

    protected abstract TState CreateState();

    protected abstract JsonSerializerOptions CreateStorageSourceGenStateOnlyOptions(StoragePayloadLayout payloadLayout);

    protected abstract JsonSerializerOptions CreateStorageSourceGenClosedTypeOptions(StoragePayloadLayout payloadLayout);

    private byte[] GetLayoutJson()
        => PayloadLayout == StoragePayloadLayout.Enveloped ? _envelopedJsonUtf8 : _flattenedJsonUtf8;

    private JsonSerializerOptions GetReflectionOptions()
        => PayloadLayout == StoragePayloadLayout.Enveloped
            ? _envelopedReflectionOptions
            : _flattenedReflectionOptions;

    private JsonSerializerOptions GetSourceGenStateOnlyOptions()
        => PayloadLayout == StoragePayloadLayout.Enveloped
            ? _envelopedSourceGenStateOnlyOptions
            : _flattenedSourceGenStateOnlyOptions;

    private JsonSerializerOptions GetSourceGenClosedTypeOptions()
        => PayloadLayout == StoragePayloadLayout.Enveloped
            ? _envelopedSourceGenClosedTypeOptions
            : _flattenedSourceGenClosedTypeOptions;
}

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Config(typeof(PerfBenchmarkConfig))]
public class MinimalStatePayloadLayoutBenchmarks : StoragePayloadLayoutBenchmarksBase<MinimalState>
{
    protected override MinimalState CreateState()
        => new() { DisplayName = "alice" };

    protected override JsonSerializerOptions CreateStorageSourceGenStateOnlyOptions(StoragePayloadLayout payloadLayout)
        => new JsonSerializerOptions(MinimalStateOnlyStorageJsonContext.Default.Options)
            .AddStateMigrationSupport(payloadLayout: payloadLayout);

    protected override JsonSerializerOptions CreateStorageSourceGenClosedTypeOptions(StoragePayloadLayout payloadLayout)
        => new JsonSerializerOptions(MinimalStateClosedStorageJsonContext.Default.Options)
            .AddStateMigrationSupport(payloadLayout: payloadLayout);
}

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Config(typeof(PerfBenchmarkConfig))]
public class ComplexStatePayloadLayoutBenchmarks : StoragePayloadLayoutBenchmarksBase<ComplexState>
{
    protected override ComplexState CreateState()
        => new()
        {
            TenantId = "tenant-alpha",
            AggregateId = "cart-000123",
            Revision = 42,
            UpdatedUtc = new DateTimeOffset(2026, 2, 25, 12, 0, 0, TimeSpan.Zero),
            BillingAddress = new ComplexAddress
            {
                Street = "Main Street 1",
                City = "Copenhagen",
                PostalCode = "2100",
                CountryCode = "DK",
            },
            ShippingAddress = new ComplexAddress
            {
                Street = "Market Street 40",
                City = "Aarhus",
                PostalCode = "8000",
                CountryCode = "DK",
            },
            Items =
            [
                new ComplexLineItem { Sku = "A-100", Quantity = 2, UnitPrice = 12.5m },
                new ComplexLineItem { Sku = "B-201", Quantity = 4, UnitPrice = 9.99m },
                new ComplexLineItem { Sku = "C-876", Quantity = 1, UnitPrice = 105.25m },
                new ComplexLineItem { Sku = "D-007", Quantity = 8, UnitPrice = 1.75m },
                new ComplexLineItem { Sku = "E-333", Quantity = 3, UnitPrice = 18.40m },
                new ComplexLineItem { Sku = "F-512", Quantity = 6, UnitPrice = 7.15m },
            ],
            Counters = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["emailsSent"] = 17,
                ["discountsApplied"] = 3,
                ["itemsRepriced"] = 11,
                ["idempotencyHits"] = 2,
            },
            Tags = ["vip", "priority", "newsletter"],
        };

    protected override JsonSerializerOptions CreateStorageSourceGenStateOnlyOptions(StoragePayloadLayout payloadLayout)
        => new JsonSerializerOptions(ComplexStateOnlyStorageJsonContext.Default.Options)
            .AddStateMigrationSupport(payloadLayout: payloadLayout);

    protected override JsonSerializerOptions CreateStorageSourceGenClosedTypeOptions(StoragePayloadLayout payloadLayout)
        => new JsonSerializerOptions(ComplexStateClosedStorageJsonContext.Default.Options)
            .AddStateMigrationSupport(payloadLayout: payloadLayout);
}

[Alias("perf/minimal-state")]
public sealed class MinimalState
{
    public string DisplayName { get; init; } = string.Empty;
}

[Alias("perf/complex-state")]
public sealed class ComplexState
{
    public string TenantId { get; init; } = string.Empty;

    public string AggregateId { get; init; } = string.Empty;

    public int Revision { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; }

    public ComplexAddress BillingAddress { get; init; } = new();

    public ComplexAddress ShippingAddress { get; init; } = new();

    public List<ComplexLineItem> Items { get; init; } = [];

    public Dictionary<string, int> Counters { get; init; } = new(StringComparer.Ordinal);

    public List<string> Tags { get; init; } = [];
}

public sealed class ComplexAddress
{
    public string Street { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string PostalCode { get; init; } = string.Empty;

    public string CountryCode { get; init; } = string.Empty;
}

public sealed class ComplexLineItem
{
    public string Sku { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public decimal UnitPrice { get; init; }
}

[JsonSerializable(typeof(MinimalState))]
internal partial class MinimalStateJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(MinimalState))]
internal partial class MinimalStateOnlyStorageJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(MinimalState))]
[JsonSerializable(typeof(Storage<MinimalState>))]
internal partial class MinimalStateClosedStorageJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(ComplexState))]
internal partial class ComplexStateJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(ComplexState))]
internal partial class ComplexStateOnlyStorageJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(ComplexState))]
[JsonSerializable(typeof(Storage<ComplexState>))]
internal partial class ComplexStateClosedStorageJsonContext : JsonSerializerContext;

public sealed class PerfBenchmarkConfig : ManualConfig
{
    public PerfBenchmarkConfig()
        => AddJob(Job.Default.WithToolchain(InProcessNoEmitToolchain.Instance));
}
