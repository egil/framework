using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.SystemTextJson.Migration.Tests;

public class JsonSerializerOptionsCombinationsTests
{
    private static readonly bool[] BoolValues = [false, true];
    private static readonly JsonNamingPolicy?[] NamingPolicies = [null, JsonNamingPolicy.CamelCase];
    private static readonly JsonNumberHandling[] NumberHandlingValues =
    [
        JsonNumberHandling.Strict,
        JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString,
    ];
    private static readonly JsonIgnoreCondition[] IgnoreConditionValues =
    [
        JsonIgnoreCondition.Never,
        JsonIgnoreCondition.WhenWritingNull,
    ];

    public static TheoryData<OptionsCombination> OptionsCombinations => BuildOptionsCombinations();

    [Theory]
    [MemberData(nameof(OptionsCombinations))]
    public void Serialize_and_deserialize_without_migration_works_for_all_option_combinations(OptionsCombination combination)
    {
        var options = CreateOptions(combination);
        var current = new OptionsMatrixV2("Jane", "Doe", 42, null);

        var json = JsonSerializer.Serialize(current, options);
        var deserialized = JsonSerializer.Deserialize<OptionsMatrixV2>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(current.FirstName, deserialized.FirstName);
        Assert.Equal(current.LastName, deserialized.LastName);
        Assert.Equal(current.Age, deserialized.Age);
        Assert.Equal(current.NickName, deserialized.NickName);
        Assert.False(deserialized.MigratedDuringDeserialization);
    }

    [Theory]
    [MemberData(nameof(OptionsCombinations))]
    public void Serialize_and_deserialize_with_migration_works_for_all_option_combinations(OptionsCombination combination)
    {
        var options = CreateOptions(combination);
        var legacy = new OptionsMatrixV1("Jane Doe", 42, null);

        var json = JsonSerializer.Serialize(legacy, options);
        var migrated = JsonSerializer.Deserialize<OptionsMatrixV2>(json, options);

        Assert.NotNull(migrated);
        Assert.Equal("Jane", migrated.FirstName);
        Assert.Equal("Doe", migrated.LastName);
        Assert.Equal(legacy.Age, migrated.Age);
        Assert.Equal(legacy.NickName, migrated.NickName);
        Assert.True(migrated.MigratedDuringDeserialization);
    }

    [Fact]
    public void Serialize_without_migration_applies_selected_output_options()
    {
        var options = CreateOutputSensitiveOptions();
        var current = new OptionsMatrixV2("Jane", "Doe", 42, null);

        var json = JsonSerializer.Serialize(current, options);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Contains('\n', json);
        Assert.Equal(typeof(OptionsMatrixV2).FullName, root.GetProperty("$type").GetString());
        Assert.Equal("Jane", root.GetProperty("firstName").GetString());
        Assert.Equal("Doe", root.GetProperty("lastName").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("age").ValueKind);
        Assert.Equal("42", root.GetProperty("age").GetString());
        Assert.False(root.TryGetProperty("nickName", out _));
        Assert.False(root.TryGetProperty("FirstName", out _));
    }

    [Fact]
    public void Deserialize_with_migration_then_serialize_applies_selected_output_options()
    {
        var options = CreateOutputSensitiveOptions();
        var legacy = new OptionsMatrixV1("Jane Doe", 42, null);

        var legacyJson = JsonSerializer.Serialize(legacy, options);
        var migrated = JsonSerializer.Deserialize<OptionsMatrixV2>(legacyJson, options);

        Assert.NotNull(migrated);
        Assert.True(migrated.MigratedDuringDeserialization);

        var migratedJson = JsonSerializer.Serialize(migrated, options);
        using var document = JsonDocument.Parse(migratedJson);
        var root = document.RootElement;

        Assert.Contains('\n', migratedJson);
        Assert.Equal(typeof(OptionsMatrixV2).FullName, root.GetProperty("$type").GetString());
        Assert.Equal("Jane", root.GetProperty("firstName").GetString());
        Assert.Equal("Doe", root.GetProperty("lastName").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("age").ValueKind);
        Assert.Equal("42", root.GetProperty("age").GetString());
        Assert.False(root.TryGetProperty("nickName", out _));
        Assert.False(root.TryGetProperty("FirstName", out _));
    }

    private static TheoryData<OptionsCombination> BuildOptionsCombinations()
    {
        var data = new TheoryData<OptionsCombination>();

        foreach (bool writeIndented in BoolValues)
        {
            foreach (bool propertyNameCaseInsensitive in BoolValues)
            {
                foreach (JsonNamingPolicy? namingPolicy in NamingPolicies)
                {
                    foreach (JsonNumberHandling numberHandling in NumberHandlingValues)
                    {
                        foreach (JsonIgnoreCondition ignoreCondition in IgnoreConditionValues)
                        {
                            data.Add(new OptionsCombination(
                                writeIndented,
                                propertyNameCaseInsensitive,
                                namingPolicy is not null,
                                numberHandling,
                                ignoreCondition));
                        }
                    }
                }
            }
        }

        return data;
    }

    private static JsonSerializerOptions CreateOptions(OptionsCombination combination)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = combination.WriteIndented,
            PropertyNameCaseInsensitive = combination.PropertyNameCaseInsensitive,
            PropertyNamingPolicy = combination.UseCamelCaseNamingPolicy ? JsonNamingPolicy.CamelCase : null,
            NumberHandling = combination.NumberHandling,
            DefaultIgnoreCondition = combination.DefaultIgnoreCondition,
        };

        options.AddJsonMigrationSupport();
        return options;
    }

    private static JsonSerializerOptions CreateOutputSensitiveOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        options.AddJsonMigrationSupport();
        return options;
    }

    public readonly record struct OptionsCombination(
        bool WriteIndented,
        bool PropertyNameCaseInsensitive,
        bool UseCamelCaseNamingPolicy,
        JsonNumberHandling NumberHandling,
        JsonIgnoreCondition DefaultIgnoreCondition)
    {
        public override string ToString()
            => $"Indented={WriteIndented},CaseInsensitive={PropertyNameCaseInsensitive},CamelCase={UseCamelCaseNamingPolicy},NumberHandling={NumberHandling},Ignore={DefaultIgnoreCondition}";
    }
}

[JsonMigratable]
public record class OptionsMatrixV1(string Name, int Age, string? NickName);

[JsonMigratable]
public record class OptionsMatrixV2(string FirstName, string LastName, int Age, string? NickName) :
    IJsonMigrationTracked,
    IMigrateFrom<OptionsMatrixV1, OptionsMatrixV2>
{
    [JsonIgnore]
    public bool MigratedDuringDeserialization { get; set; }

    public static bool TryMigrateFrom(OptionsMatrixV1 source, out OptionsMatrixV2 result)
    {
        var names = source.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        result = new OptionsMatrixV2(
            names.Length > 0 ? names[0] : string.Empty,
            names.Length > 1 ? names[1] : string.Empty,
            source.Age,
            source.NickName);
        return true;
    }
}
