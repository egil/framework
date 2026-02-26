using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Tests;

public class SamplesTest
{
    private readonly JsonSerializerOptions options;

    public SamplesTest()
    {
        options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonMigratableConverterFactory());
        options.TypeInfoResolverChain.Add(TestJsonSerializationContext.Default);
        options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
    }

    [Fact]
    public void Deserialize_from_migratable_untyped_source()
    {
        var deserialized = JsonSerializer.Deserialize<SampleRecord1>("""
            {"Name":"Egil Hansen","Age":42}
            """,
            options);

        Assert.NotNull(deserialized);
        Assert.Equal("Egil Hansen", deserialized.Name);
        Assert.Equal(42, deserialized.Age);
    }

    [Fact]
    public void Migrate_between_migratable_using_IMigrate()
    {
        var sample1 = new SampleRecord1("Egil Hansen", 42);
        var sample1Json = JsonSerializer.Serialize(sample1, options);

        var sample2 = JsonSerializer.Deserialize<SampleRecord3>(sample1Json, options);

        Assert.NotNull(sample2);
        Assert.Equal("Egil", sample2.FirstName);
        Assert.Equal("Hansen", sample2.LastName);
        Assert.Equal(42, sample2.Age);
    }

    [Fact]
    public void Migrate_between_migratable_using_Static_TryFrom()
    {
        var sample1 = new SampleRecord1("Egil Hansen", 42);
        var sample1Json = JsonSerializer.Serialize(sample1, options);

        var sample2 = JsonSerializer.Deserialize<SampleRecord2>(sample1Json, options);

        Assert.NotNull(sample2);
        Assert.Equal("Egil", sample2.FirstName);
        Assert.Equal("Hansen", sample2.LastName);
        Assert.Equal(42, sample2.Age);
        Assert.True(sample2.MigratedDuringDeserialization);
    }

    [Fact]
    public void Serialize_migratable_object()
    {
        var sample = new SampleRecord2("Egil", "Hansen", 42);
        var sample2Json = JsonSerializer.Serialize(sample, options);
        Assert.Equal(
            """
            {"$type":"Egil.SystemTextJson.Migration.Tests.SampleRecord2","firstName":"Egil","lastName":"Hansen","age":42}
            """,
            sample2Json);

        var sample3Json = JsonSerializer.Serialize(new SampleRecord3("foo", "bar", 42), options);
        Assert.Equal(
            """
            {"$migrationType":"Egil.SystemTextJson.Migration.Tests.SampleRecord3","firstName":"foo","lastName":"bar","age":42}
            """,
            sample3Json);

        var sample4Json = JsonSerializer.Serialize(new SampleRecord4
        {
            FirstName = "foo",
            LastName = "bar",
            Age = 42
        }, options);

        Assert.Equal(
            """
            {"$type":"Sample4","age":42,"lastName":"bar","firstName":"foo"}
            """,
            sample4Json);
    }

    [Fact]
    public void Supporting_onjson_interfaces()
    {
        var original = new SampleRecord4
        {
            FirstName = "foo",
            LastName = "bar",
            Age = 42
        };

        var sample4Json = JsonSerializer.Serialize(original, options);

        var deserialized = JsonSerializer.Deserialize<SampleRecord4>(sample4Json, options);

        Assert.NotNull(deserialized);
        Assert.True(original.OnSerializingCalled);
        Assert.True(original.OnSerializedCalled);
        Assert.True(deserialized.OnDeserializingCalled);
        Assert.True(deserialized.OnDeserializedCalled);
    }
}

[JsonMigratable]
public record class SampleRecord1(string Name, int Age);

[JsonMigratable]
public record class SampleRecord2(string FirstName, string LastName, int Age)
{
    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; init; }

    // duck typing matching
    public static bool TryMigrateFrom(SampleRecord1 source, out SampleRecord2 result)
    {
        var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstName = names.Length > 0 ? names[0] : string.Empty;
        var lastName = names.Length > 1 ? names[1] : string.Empty;

        result = new SampleRecord2(firstName, lastName, source.Age)
        {
            MigratedDuringDeserialization = true
        };

        return true;
    }
}

[JsonMigratable(TypeDiscriminatorPropertyName = "$migrationType")]
public record class SampleRecord3(string FirstName, string LastName, int Age);

[JsonMigratable(TypeDiscriminator = "Sample4")]
public record class SampleRecord4 : IJsonOnSerializing, IJsonOnSerialized, IJsonOnDeserializing, IJsonOnDeserialized
{
    [JsonPropertyOrder(1)]
    public string FirstName { get; set; }

    [JsonPropertyOrder(0)]
    public string LastName { get; set; }

    [JsonPropertyOrder(int.MinValue)]
    public int Age { get; set; }

    [JsonIgnore]
    public bool OnDeserializedCalled { get; private set; }

    [JsonIgnore]
    public bool OnDeserializingCalled { get; private set; }

    [JsonIgnore]
    public bool OnSerializedCalled { get; private set; }

    [JsonIgnore]
    public bool OnSerializingCalled { get; private set; }

    public void OnDeserialized()
    {
        OnDeserializedCalled = true;
    }

    public void OnDeserializing()
    {
        OnDeserializingCalled = true;
    }

    public void OnSerialized()
    {
        OnSerializedCalled = true;
    }

    public void OnSerializing()
    {
        OnSerializingCalled = true;
    }
}

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(SampleRecord3))]
public partial class TestJsonSerializationContext : JsonSerializerContext;

public class SampleMigrator :
    IMigrate<SampleRecord1, SampleRecord2>,
    IMigrate<SampleRecord1, SampleRecord3>,
    IMigrate<SampleRecord2, SampleRecord3>
{
    public bool TryMigrateFrom(SampleRecord1 source, out SampleRecord2 result)
    {
        throw new InvalidOperationException("Should not be called - SampleRecord2 has a static TryMigrateFrom that should be used instead.");
    }

    public bool TryMigrateFrom(SampleRecord1 source, out SampleRecord3 result)
    {
        var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstName = names.Length > 0 ? names[0] : string.Empty;
        var lastName = names.Length > 1 ? names[1] : string.Empty;
        result = new SampleRecord3(firstName, lastName, source.Age);
        return true;
    }

    public bool TryMigrateFrom(SampleRecord2 source, out SampleRecord3 result)
    {
        result = new SampleRecord3(source.FirstName, source.LastName, source.Age);
        return true;
    }

}

public interface IMigrate<in TSource, TTarget>
{
    bool TryMigrateFrom(TSource source, out TTarget result);
}

internal class MigratorContext(ConcurrentDictionary<string, Type> typeDescriminatorCache)
{
    public required Type Type { get; init; }

    public required JsonTypeInfo JsonTypeInfo { get; init; }

    public required string TypeDiscriminator { get; init; }

    public required string TypeDiscriminatorPropertyName { get; init; }

    public required FrozenDictionary<string, MigratorReference> Migrators { get; init; }

    public Type? GetMigrationSourceType(string? discriminatorValue)
    {
        return discriminatorValue is not null
            ? typeDescriminatorCache.GetValueOrDefault(discriminatorValue)
            : null;
    }
}

internal class MigratorReference
{
    public required string TypeDiscriminator { get; init; }

    public required string TypeDiscriminatorPropertyName { get; init; }

    public required MethodInfo MigratorMethod { get; init; }

    public virtual T InvokeMigrator<T>(object? source)
    {
        object?[] args = [source, null];
        var success = (bool)MigratorMethod.Invoke(null, args)!;
        return success && args[1] is T result
            ? result
            : default!;
    }
}

internal class InstanceMigratorReference : MigratorReference
{
    public required object MigratorType { get; init; }

    public override T InvokeMigrator<T>(object? source)
    {
        object?[] args = [source, null];
        var success = (bool)MigratorMethod.Invoke(MigratorType, args)!;
        return success && args[1] is T result
            ? result
            : default!;
    }
}

internal class JsonMigratableConverterFactory : JsonConverterFactory
{
    private readonly ConcurrentDictionary<string, MigratorContext> typeInfoCache = new();
    private readonly ConcurrentDictionary<string, Type> typeDescriminatorCache = new();

    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.GetCustomAttribute<JsonMigratableAttribute>(inherit: true) is not null;

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var migratableAttribute = typeToConvert.GetCustomAttribute<JsonMigratableAttribute>(inherit: true);
        if (migratableAttribute is null)
        {
            return null;
        }

        var typeDescriminator = migratableAttribute.TypeDiscriminator ?? typeToConvert.FullName ?? typeToConvert.Name;
        typeDescriminatorCache.TryAdd(typeDescriminator, typeToConvert);

        var migrationInfo = typeInfoCache.GetOrAdd(
            typeDescriminator,
            typeDesc =>
            {
                // Clone the options to avoid modifying the original options instance
                // and to remove this converter from the clone to prevent infinite
                // recursion when creating the JsonTypeInfo.
                var clone = new JsonSerializerOptions(options);
                clone.Converters.Remove(this);

                // Create a JsonTypeInfo for the type and add a property for the type discriminator.
                var typeInfo = clone.GetTypeInfo(typeToConvert);
                var typePropertyName = typeInfo.CreateJsonPropertyInfo(typeof(string), migratableAttribute.TypeDiscriminatorPropertyName);
                typePropertyName.Order = int.MinValue;
                typePropertyName.Get = _ => migratableAttribute.TypeDiscriminator ?? typeToConvert.FullName ?? typeToConvert.Name;
                typePropertyName.IsRequired = false;

                // Insert the type discriminator property at the beginning of the
                // properties list to ensure it is serialized first, even if other properties
                // have int.MinValue too.
                typeInfo.Properties.Insert(0, typePropertyName);

                var staticMigrators = typeToConvert
                    .GetMethods()
                    .Where(m => m.IsStatic)
                    .Where(m => m.Name.Equals("TryMigrateFrom", StringComparison.Ordinal))
                    .Where(m => m.ReturnType == typeof(bool))
                    .Where(m =>
                    {
                        if (m.GetParameters() is [{ }, { IsOut: true } resultParam])
                        {
                            var outType = resultParam.ParameterType.IsByRef
                                ? resultParam.ParameterType.GetElementType()
                                : resultParam.ParameterType;
                            return outType == typeToConvert;
                        }

                        return false;
                    })
                    .Select(fromMethod =>
                    {
                        var sourceType = fromMethod.GetParameters()[0].ParameterType;

                        var attr = sourceType.GetCustomAttribute<JsonMigratableAttribute>(inherit: true);
                        var prop = attr?.TypeDiscriminatorPropertyName;
                        var sourceTypeDescriminator = attr?.TypeDiscriminator
                            ?? sourceType.FullName
                            ?? sourceType.Name;

                        var migratorRef = prop is not null && fromMethod is not null && sourceTypeDescriminator is not null
                            ? new MigratorReference()
                            {
                                TypeDiscriminator = sourceTypeDescriminator,
                                MigratorMethod = fromMethod,
                                TypeDiscriminatorPropertyName = prop,
                            }
                            : null;
                        return migratorRef;
                    })
                    .OfType<MigratorReference>();

                var instanceMigrators = Assembly
                    .GetEntryAssembly()!
                    .GetTypes()
                    .Where(x => x.IsClass)
                    .Select(type => (OwningType: type, MigrateInterfaces: type.GetInterfaces().Where(i =>
                    {
                        return i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMigrate<,>) && i.GetGenericArguments()[1] == typeToConvert;
                    })))
                    .SelectMany(tuple => tuple.MigrateInterfaces.Select(migrateInterface =>
                    {
                        var sourceType = migrateInterface.GetGenericArguments()[0];
                        var attr = sourceType.GetCustomAttribute<JsonMigratableAttribute>(inherit: true);
                        var prop = attr?.TypeDiscriminatorPropertyName;
                        var sourceTypeDescriminator = attr?.TypeDiscriminator
                            ?? sourceType.FullName
                            ?? sourceType.Name;
                        var interfaceMap = tuple.OwningType.GetInterfaceMap(migrateInterface);
                        var migratorMethod = interfaceMap.TargetMethods[0];
                        var migratorRef = prop is not null && migratorMethod is not null && sourceTypeDescriminator is not null
                            ? new InstanceMigratorReference()
                            {
                                TypeDiscriminatorPropertyName = prop,
                                TypeDiscriminator = sourceTypeDescriminator,
                                MigratorMethod = migratorMethod,
                                MigratorType = Activator.CreateInstance(tuple.OwningType),
                            }
                            : null;
                        return migratorRef;
                    }).OfType<MigratorReference>());

                var migrators = new Dictionary<string, MigratorReference>(StringComparer.Ordinal);

                foreach (var migrator in instanceMigrators)
                {
                    migrators[migrator.TypeDiscriminator] = migrator;
                }

                // add static migrators last os they take precedence over instance
                // migrators if both exist for the same source type
                foreach (var migrator in staticMigrators)
                {
                    migrators[migrator.TypeDiscriminator] = migrator;
                }

                return new MigratorContext(typeDescriminatorCache)
                {
                    Type = typeToConvert,
                    JsonTypeInfo = typeInfo,
                    TypeDiscriminator = typeDescriminator,
                    TypeDiscriminatorPropertyName = migratableAttribute.TypeDiscriminatorPropertyName,
                    Migrators = migrators.ToFrozenDictionary(StringComparer.Ordinal)
                };
            });

        var converterType = typeof(JsonMigratableConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType, migrationInfo)!;
    }
}

internal sealed class JsonMigratableConverter<T>(MigratorContext migrationInfo) : JsonConverter<T>
{
    private readonly JsonTypeInfo<T>? typedJsonTypeInfo = migrationInfo.JsonTypeInfo as JsonTypeInfo<T>;
    private readonly byte[] typePropertyNameUtf8 = Encoding.UTF8.GetBytes(migrationInfo.TypeDiscriminatorPropertyName);
    private readonly byte[] typeDiscriminatorUtf8 = Encoding.UTF8.GetBytes(migrationInfo.TypeDiscriminator);

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (TryMatchExpectedType(reader, typeToConvert, options, out var discriminatorValue))
        {
            return typedJsonTypeInfo is not null
               ? JsonSerializer.Deserialize(ref reader, typedJsonTypeInfo)
               : (T?)JsonSerializer.Deserialize(ref reader, typeToConvert);
        }

        if (discriminatorValue is null || migrationInfo.GetMigrationSourceType(discriminatorValue) is not Type migrationSourceType)
        {
            throw new JsonException("Unknown type discriminator value: " + discriminatorValue);
        }

        if (!migrationInfo.Migrators.TryGetValue(discriminatorValue, out var migrator))
        {
            throw new JsonException("No migrator found for type discriminator value: " + discriminatorValue);
        }

        var migrationSource = JsonSerializer.Deserialize(ref reader, migrationSourceType, options);
        var result = migrator.InvokeMigrator<T>(migrationSource);

        return result;
    }

    private bool TryMatchExpectedType(Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options, out string? discriminatorValue)
    {
        discriminatorValue = null;
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected start of object, but got {reader.TokenType}.");
        }

        if (!reader.Read() || reader.TokenType == JsonTokenType.EndObject)
        {
            throw new JsonException("Unexpected end of JSON.");
        }

        if (reader.TokenType != JsonTokenType.PropertyName)
        {
            throw new JsonException($"Expected property for type discriminator, but got {reader.TokenType}.");
        }

        if (!reader.ValueTextEquals(typePropertyNameUtf8))
        {
            // No type discriminator property found? Assume the JSON is for type T,
            // but json was serialized with a version of T that did not have the JsonMigration attribute.
            if (!TryFindMigratorDescriminator(ref reader))
            {
                return true;
            }
        }

        if (!reader.Read() || reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected string value for type discriminator, but got {reader.TokenType}.");
        }

        // Matching type discriminator value found, but does it match the expected type discriminator for T?
        if (reader.ValueTextEquals(typeDiscriminatorUtf8) && reader.Read())
        {
            return true;
        }

        discriminatorValue = reader.GetString();
        return false;
    }

    private bool TryFindMigratorDescriminator(ref Utf8JsonReader reader)
    {
        var propertyName = reader.GetString();

        if (propertyName is null)
        {
            return false;
        }

        foreach (var migrator in migrationInfo.Migrators)
        {
            if (propertyName.Equals(migrator.Value.TypeDiscriminatorPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (typedJsonTypeInfo is not null)
        {
            JsonSerializer.Serialize(writer, value, typedJsonTypeInfo);
        }
        else
        {
            JsonSerializer.Serialize(writer, value, migrationInfo.JsonTypeInfo);
        }
    }
}