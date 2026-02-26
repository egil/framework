using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Tests;

public class SamplesTest
{
    private readonly JsonSerializerOptions options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    [Fact]
    public void Migrate_from_untyped_source()
    {
        var sample = new SampleRecord1("Egil Hansen", 42);
        var json = JsonSerializer.Serialize(sample, options);
        Assert.Equal(
            """
            {"Name":"Egil Hansen","Age":42}
            """,
            json);

        var deserialized = JsonSerializer.Deserialize<SampleRecord2>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal("Egil", deserialized!.FirstName);
        Assert.Equal("Hansen", deserialized.LastName);
        Assert.Equal(42, deserialized.Age);
    }

    [Fact]
    public void Serialize_migratable_object()
    {
        var sample = new SampleRecord2("Egil", "Hansen", 42);
        var json = JsonSerializer.Serialize(sample, options);
        Assert.Equal(
            """
            {"Name":"Egil Hansen","Age":42}
            """,
            json);

        // JsonTypeInfo<T>
    }

    internal static JsonPolymorphismOptions? CreateFromAttributeDeclarations(JsonPolymorphismOptions? options, Type baseType)
    {
        options ??= new();
        options.DerivedTypes.Add(new JsonDerivedType(baseType, baseType.FullName ?? baseType.Name));
        return options;
    }
}


public record class SampleRecord1(string Name, int Age);

[JsonMigratable]
public record class SampleRecord2(string FirstName, string LastName, int Age) : IMigratable<SampleRecord1, SampleRecord2>
{
    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; init; }

    public static SampleRecord2 From(SampleRecord1 source)
    {
        var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstName = names.Length > 0 ? names[0] : string.Empty;
        var lastName = names.Length > 1 ? names[1] : string.Empty;

        return new SampleRecord2(firstName, lastName, source.Age)
        {
            MigratedDuringDeserialization = true
        };
    }
}

[JsonMigratable]
public record class SampleRecord3(string FirstName, string LastName, int Age);

public class SampleMigrator : IMigrate<SampleRecord1, SampleRecord3>
{
    public SampleRecord3 Migrate(SampleRecord1 source)
    {
        var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstName = names.Length > 0 ? names[0] : string.Empty;
        var lastName = names.Length > 1 ? names[1] : string.Empty;

        return new SampleRecord3(firstName, lastName, source.Age);
    }
}

public interface IMigratable<in TSource, TTarget> where TTarget : IMigratable<TSource, TTarget>
{
    static abstract TTarget From(TSource source);
}

public interface IMigrate<in TSource, out TTarget>
{
    TTarget Migrate(TSource source);
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public class JsonMigratableAttribute : JsonConverterAttribute
{
    public string TypeNamePropertyName { get; set; } = "$type";

    public string? TypeDiscriminator { get; set; }

    public override JsonConverter CreateConverter(Type typeToConvert)
    {
        var converterType = typeof(JsonMigratableConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(
            converterType,
            TypeNamePropertyName,
            TypeDiscriminator ?? typeToConvert.FullName ?? typeToConvert.Name)!;
    }
}

internal sealed class JsonMigratableConverter<T>(string typeNamePropertyName, string typeDiscriminator) : JsonConverter<T>
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotImplementedException();

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(typeNamePropertyName, typeDiscriminator);
        var typeInfo = options.GetTypeInfo(typeof(T));

        foreach (var property in typeInfo.Properties.OrderBy(x => x.Order))
        {
            var propertyValue = property.Get(value);
            if (!property.ShouldSerialize(value, propertyValue))
            {
                continue;
            }

            writer.WritePropertyName(property.Name);
            JsonSerializer.Serialize(writer, propertyValue, property.PropertyType, options);
        }

        writer.WriteEndObject();
    }
}