using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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
    private JsonSerializerOptions _storageHardcodedSourceGenOptions = null!;
    private JsonSerializerOptions _storagePolymorphicSourceGenOptions = null!;
    private TState _state = null!;
    private Storage<TState> _storageState = null!;
    private byte[] _plainJsonUtf8 = null!;
    private byte[] _storageJsonUtf8 = null!;
    private byte[] _storageHardcodedJsonUtf8 = null!;
    private byte[] _storagePolymorphicJsonUtf8 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _plainReflectionOptions = new JsonSerializerOptions();
        _storageReflectionOptions = new JsonSerializerOptions().AddStateMigrationSupport();
        _storageSourceGenStateOnlyOptions = CreateStorageSourceGenStateOnlyOptions();
        _storageSourceGenClosedTypeOptions = CreateStorageSourceGenClosedTypeOptions();
        _storageHardcodedSourceGenOptions = CreateStorageHardcodedSourceGenOptions();
        _storagePolymorphicSourceGenOptions = CreateStoragePolymorphicSourceGenOptions();

        _state = CreateState();
        _storageState = new Storage<TState> { Value = _state };

        _plainJsonUtf8 = JsonSerializer.SerializeToUtf8Bytes(_state, _plainReflectionOptions);
        _storageJsonUtf8 = JsonSerializer.SerializeToUtf8Bytes(_storageState, _storageReflectionOptions);
        _storageHardcodedJsonUtf8 = JsonSerializer.SerializeToUtf8Bytes(_storageState, _storageHardcodedSourceGenOptions);
        _storagePolymorphicJsonUtf8 = JsonSerializer.SerializeToUtf8Bytes(_storageState, _storagePolymorphicSourceGenOptions);
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
    [BenchmarkCategory("Deserialize", "SourceGen", "Hardcoded")]
    public Storage<TState> StateMigrationDeserializeNoMigrationHardcodedSourceGen()
        => JsonSerializer.Deserialize<Storage<TState>>(_storageHardcodedJsonUtf8, _storageHardcodedSourceGenOptions)!;

    [Benchmark]
    [BenchmarkCategory("Deserialize", "SourceGen", "Polymorphic")]
    public Storage<TState> StateMigrationDeserializeNoMigrationStjPolymorphicSourceGen()
        => JsonSerializer.Deserialize<Storage<TState>>(_storagePolymorphicJsonUtf8, _storagePolymorphicSourceGenOptions)!;

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

    [Benchmark]
    [BenchmarkCategory("Serialize", "SourceGen", "Hardcoded")]
    public byte[] StateMigrationSerializeHardcodedSourceGen()
        => JsonSerializer.SerializeToUtf8Bytes(_storageState, _storageHardcodedSourceGenOptions);

    [Benchmark]
    [BenchmarkCategory("Serialize", "SourceGen", "Polymorphic")]
    public byte[] StateMigrationSerializeStjPolymorphicSourceGen()
        => JsonSerializer.SerializeToUtf8Bytes(_storageState, _storagePolymorphicSourceGenOptions);

    protected abstract TState CreateState();

    protected abstract byte[] SerializePlainSourceGen(TState state);

    protected abstract TState DeserializePlainSourceGen(byte[] utf8Json);

    protected abstract JsonSerializerOptions CreateStorageSourceGenStateOnlyOptions();

    protected abstract JsonSerializerOptions CreateStorageSourceGenClosedTypeOptions();

    protected abstract JsonSerializerOptions CreateStorageHardcodedSourceGenOptions();

    protected abstract JsonSerializerOptions CreateStoragePolymorphicSourceGenOptions();
}

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Config(typeof(PerfBenchmarkConfig))]
public class MinimalStateNoMigrationBenchmarks : StorageNoMigrationBenchmarksBase<MinimalState>
{
    private const string MinimalCurrentTypeId = "perf/minimal-state";
    private const string MinimalLegacyTypeId = "perf/minimal-legacy-state";

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

    protected override JsonSerializerOptions CreateStorageHardcodedSourceGenOptions()
    {
        JsonSerializerOptions options = new(MinimalStateHardcodedStorageJsonContext.Default.Options);
        options.Converters.Insert(
            0,
            new HardcodedStorageJsonConverter<MinimalState, MinimalLegacyState>(
                MinimalCurrentTypeId,
                MinimalLegacyTypeId,
                MinimalStateHardcodedStorageJsonContext.Default.MinimalState,
                MinimalStateHardcodedStorageJsonContext.Default.MinimalLegacyState,
                static legacy => new MinimalState { DisplayName = legacy.Name }));
        return options;
    }

    protected override JsonSerializerOptions CreateStoragePolymorphicSourceGenOptions()
    {
        JsonSerializerOptions options = new(MinimalStatePolymorphicStorageJsonContext.Default.Options);
        options.Converters.Insert(
            0,
            new PolymorphicStorageJsonConverter<MinimalState, IMinimalStatePolymorphic>(
                MinimalStatePolymorphicStorageJsonContext.Default.IMinimalStatePolymorphic,
                static state => state switch
                {
                    MinimalState current => current,
                    MinimalLegacyState legacy => new MinimalState { DisplayName = legacy.Name },
                    _ => throw new JsonException("Unsupported polymorphic minimal state."),
                }));
        return options;
    }
}

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Config(typeof(PerfBenchmarkConfig))]
public class ComplexStateNoMigrationBenchmarks : StorageNoMigrationBenchmarksBase<ComplexState>
{
    private const string ComplexCurrentTypeId = "perf/complex-state";
    private const string ComplexLegacyTypeId = "perf/complex-legacy-state";

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

    protected override JsonSerializerOptions CreateStorageHardcodedSourceGenOptions()
    {
        JsonSerializerOptions options = new(ComplexStateHardcodedStorageJsonContext.Default.Options);
        options.Converters.Insert(
            0,
            new HardcodedStorageJsonConverter<ComplexState, ComplexLegacyState>(
                ComplexCurrentTypeId,
                ComplexLegacyTypeId,
                ComplexStateHardcodedStorageJsonContext.Default.ComplexState,
                ComplexStateHardcodedStorageJsonContext.Default.ComplexLegacyState,
                static legacy => new ComplexState
                {
                    TenantId = legacy.TenantId,
                    AggregateId = legacy.AggregateId,
                    Revision = legacy.Revision,
                    UpdatedUtc = legacy.UpdatedUtc,
                    BillingAddress = legacy.BillingAddress,
                    ShippingAddress = legacy.ShippingAddress,
                    Items = legacy.Items,
                    Counters = legacy.Counters,
                    Tags = legacy.Tags,
                }));
        return options;
    }

    protected override JsonSerializerOptions CreateStoragePolymorphicSourceGenOptions()
    {
        JsonSerializerOptions options = new(ComplexStatePolymorphicStorageJsonContext.Default.Options);
        options.Converters.Insert(
            0,
            new PolymorphicStorageJsonConverter<ComplexState, IComplexStatePolymorphic>(
                ComplexStatePolymorphicStorageJsonContext.Default.IComplexStatePolymorphic,
                static state => state switch
                {
                    ComplexState current => current,
                    ComplexLegacyState legacy => new ComplexState
                    {
                        TenantId = legacy.TenantId,
                        AggregateId = legacy.AggregateId,
                        Revision = legacy.Revision,
                        UpdatedUtc = legacy.UpdatedUtc,
                        BillingAddress = legacy.BillingAddress,
                        ShippingAddress = legacy.ShippingAddress,
                        Items = legacy.Items,
                        Counters = legacy.Counters,
                        Tags = legacy.Tags,
                    },
                    _ => throw new JsonException("Unsupported polymorphic complex state."),
                }));
        return options;
    }
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

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(MinimalState), "perf/minimal-state")]
[JsonDerivedType(typeof(MinimalLegacyState), "perf/minimal-legacy-state")]
public interface IMinimalStatePolymorphic;

[Alias("perf/minimal-state")]
public sealed class MinimalState : IMinimalStatePolymorphic
{
    public string DisplayName { get; init; } = string.Empty;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ComplexState), "perf/complex-state")]
[JsonDerivedType(typeof(ComplexLegacyState), "perf/complex-legacy-state")]
public interface IComplexStatePolymorphic;

[Alias("perf/complex-state")]
public sealed class ComplexState : IComplexStatePolymorphic
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

public sealed class HardcodedStorageJsonConverter<TState, TLegacyState> : JsonConverter<Storage<TState>>
    where TState : class
    where TLegacyState : class
{
    private static readonly byte[] TypePropertyNameUtf8 = "$type"u8.ToArray();
    private static readonly byte[] ValuePropertyNameUtf8 = "$value"u8.ToArray();
    private readonly string _currentTypeId;
    private readonly byte[] _currentTypeIdUtf8;
    private readonly byte[] _legacyTypeIdUtf8;
    private readonly JsonTypeInfo<TState> _currentTypeInfo;
    private readonly JsonTypeInfo<TLegacyState> _legacyTypeInfo;
    private readonly Func<TLegacyState, TState> _migrateFromLegacy;

    public HardcodedStorageJsonConverter(
        string currentTypeId,
        string legacyTypeId,
        JsonTypeInfo<TState> currentTypeInfo,
        JsonTypeInfo<TLegacyState> legacyTypeInfo,
        Func<TLegacyState, TState> migrateFromLegacy)
    {
        _currentTypeId = currentTypeId;
        _currentTypeIdUtf8 = System.Text.Encoding.UTF8.GetBytes(currentTypeId);
        _legacyTypeIdUtf8 = System.Text.Encoding.UTF8.GetBytes(legacyTypeId);
        _currentTypeInfo = currentTypeInfo;
        _legacyTypeInfo = legacyTypeInfo;
        _migrateFromLegacy = migrateFromLegacy;
    }

    public override Storage<TState> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Storage payload must be a JSON object.");
        }

        if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals(TypePropertyNameUtf8))
        {
            throw new JsonException("Storage payload must start with '$type'.");
        }

        if (!reader.Read() || reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Storage payload '$type' must be a string.");
        }

        bool migrated;
        if (reader.ValueTextEquals(_currentTypeIdUtf8))
        {
            migrated = false;
        }
        else if (reader.ValueTextEquals(_legacyTypeIdUtf8))
        {
            migrated = true;
        }
        else
        {
            throw new JsonException("Storage payload type is unknown.");
        }

        if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals(ValuePropertyNameUtf8))
        {
            throw new JsonException("Storage payload must contain '$value'.");
        }

        if (!reader.Read())
        {
            throw new JsonException("Storage payload is missing '$value'.");
        }

        TState state = migrated
            ? _migrateFromLegacy(JsonSerializer.Deserialize(ref reader, _legacyTypeInfo)
                                 ?? throw new JsonException("Storage payload legacy state was null."))
            : JsonSerializer.Deserialize(ref reader, _currentTypeInfo)
              ?? throw new JsonException("Storage payload current state was null.");

        if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject)
        {
            throw new JsonException("Storage payload contains unexpected properties.");
        }

        return new Storage<TState>
        {
            Value = InvokeOnDeserializedCallback(state),
            MigratedDuringDeserialization = migrated,
        };
    }

    public override void Write(Utf8JsonWriter writer, Storage<TState> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("$type", _currentTypeId);
        writer.WritePropertyName("$value");
        JsonSerializer.Serialize(writer, value.Value, _currentTypeInfo);
        writer.WriteEndObject();
    }

    private static TState InvokeOnDeserializedCallback(TState state)
    {
        if (state is global::Orleans.Serialization.IOnDeserialized callback)
        {
            callback.OnDeserialized(default!);
        }

        return state;
    }
}

public sealed class PolymorphicStorageJsonConverter<TState, TPolymorphicState> : JsonConverter<Storage<TState>>
    where TState : class, TPolymorphicState
    where TPolymorphicState : class
{
    private static readonly byte[] ValuePropertyNameUtf8 = "$value"u8.ToArray();
    private readonly JsonTypeInfo<TPolymorphicState> _polymorphicTypeInfo;
    private readonly Func<TPolymorphicState, TState> _toCurrentState;

    public PolymorphicStorageJsonConverter(
        JsonTypeInfo<TPolymorphicState> polymorphicTypeInfo,
        Func<TPolymorphicState, TState> toCurrentState)
    {
        _polymorphicTypeInfo = polymorphicTypeInfo;
        _toCurrentState = toCurrentState;
    }

    public override Storage<TState> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Storage payload must be a JSON object.");
        }

        if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals(ValuePropertyNameUtf8))
        {
            throw new JsonException("Storage payload must contain '$value'.");
        }

        if (!reader.Read())
        {
            throw new JsonException("Storage payload is missing '$value'.");
        }

        TPolymorphicState state = JsonSerializer.Deserialize(ref reader, _polymorphicTypeInfo)
                                  ?? throw new JsonException("Storage payload state was null.");
        TState current = _toCurrentState(state);

        if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject)
        {
            throw new JsonException("Storage payload contains unexpected properties.");
        }

        return new Storage<TState>
        {
            Value = InvokeOnDeserializedCallback(current),
            MigratedDuringDeserialization = state is not TState,
        };
    }

    public override void Write(Utf8JsonWriter writer, Storage<TState> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("$value");
        JsonSerializer.Serialize(writer, (TPolymorphicState)value.Value, _polymorphicTypeInfo);
        writer.WriteEndObject();
    }

    private static TState InvokeOnDeserializedCallback(TState state)
    {
        if (state is global::Orleans.Serialization.IOnDeserialized callback)
        {
            callback.OnDeserialized(default!);
        }

        return state;
    }
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

[Alias("perf/minimal-legacy-state")]
public sealed class MinimalLegacyState : IMinimalStatePolymorphic
{
    public string Name { get; init; } = string.Empty;
}

[Alias("perf/complex-legacy-state")]
public sealed class ComplexLegacyState : IComplexStatePolymorphic
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

[JsonSerializable(typeof(MinimalState))]
[JsonSerializable(typeof(MinimalLegacyState))]
[JsonSerializable(typeof(Storage<MinimalState>))]
internal partial class MinimalStateHardcodedStorageJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(IMinimalStatePolymorphic))]
[JsonSerializable(typeof(Storage<MinimalState>))]
internal partial class MinimalStatePolymorphicStorageJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(ComplexState))]
[JsonSerializable(typeof(ComplexLegacyState))]
[JsonSerializable(typeof(Storage<ComplexState>))]
internal partial class ComplexStateHardcodedStorageJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(IComplexStatePolymorphic))]
[JsonSerializable(typeof(Storage<ComplexState>))]
internal partial class ComplexStatePolymorphicStorageJsonContext : JsonSerializerContext;

public sealed class PerfBenchmarkConfig : ManualConfig
{
    public PerfBenchmarkConfig()
        => AddJob(Job.Default.WithToolchain(InProcessNoEmitToolchain.Instance));
}
