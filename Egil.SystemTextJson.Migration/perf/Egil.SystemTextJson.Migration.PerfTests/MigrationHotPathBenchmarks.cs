using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;

namespace Egil.SystemTextJson.Migration.PerfTests;

[MemoryDiagnoser]
[Config(typeof(PerfBenchmarkConfig))]
public class StructMigrationHotPathBenchmarks
{
    private JsonSerializerOptions plainOptions = null!;
    private JsonSerializerOptions staticOptions = null!;
    private JsonSerializerOptions externalOptions = null!;
    private static readonly byte[] Payload = "{\"$type\":\"struct-v1\",\"value\":42}"u8.ToArray();

    [Params(false, true)]
    public bool SourceGeneration { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        plainOptions = HotPathOptions.Create(SourceGeneration);
        staticOptions = HotPathOptions.Create(SourceGeneration).AddJsonMigrationSupport();
        externalOptions = HotPathOptions.Create(SourceGeneration)
            .AddJsonMigrationSupport(builder => builder.RegisterMigrator<PerfStructMigrator>());

        if (Manual().Value != 43 || Static().Value != 43 || External().Value != 43)
        {
            throw new InvalidOperationException("Struct benchmark must execute a successful migration.");
        }
    }

    [Benchmark(Baseline = true)]
    public PerfStructStaticTarget Manual()
    {
        var source = JsonSerializer.Deserialize<PerfStructSource>(Payload, plainOptions);
        PerfStructStaticTarget.TryMigrateFrom(source, out var result);
        return result;
    }

    [Benchmark]
    public PerfStructStaticTarget Static() => JsonSerializer.Deserialize<PerfStructStaticTarget>(Payload, staticOptions);

    [Benchmark]
    public PerfStructExternalTarget External() => JsonSerializer.Deserialize<PerfStructExternalTarget>(Payload, externalOptions);
}

[MemoryDiagnoser]
[Config(typeof(PerfBenchmarkConfig))]
public class VersionsMigrationHotPathBenchmarks
{
    private JsonSerializerOptions options = null!;
    private byte[] payload = null!;

    [Params(false, true)]
    public bool SourceGeneration { get; set; }

    [ParamsSource(nameof(Positions))]
    public VersionPosition Position { get; set; }

    [Params(DiscriminatorLayout.SharedPrefix, DiscriminatorLayout.DifferentLengths)]
    public DiscriminatorLayout Layout { get; set; }

    // The first, middle and last version number for 4 and 8 registered sources. A single source
    // has only one position, so it is listed once rather than three times under different names.
    public static IEnumerable<VersionPosition> Positions() =>
    [
        new(1, 1),
        new(1, 4), new(2, 4), new(4, 4),
        new(1, 8), new(4, 8), new(8, 8),
    ];

    [GlobalSetup]
    public void Setup()
    {
        options = HotPathOptions.Create(SourceGeneration).AddJsonMigrationSupport(builder =>
        {
            builder.GetTypeDiscriminatorFrom<JsonMigratableAttribute>(attribute => Discriminator(attribute.TypeDiscriminator!));
            builder.RegisterMigrator<PerfVersion1, PerfVersionTarget, PerfVersionMigrator<PerfVersion1>>();
            if (Position.SourceCount >= 4)
            {
                builder.RegisterMigrator<PerfVersion2, PerfVersionTarget, PerfVersionMigrator<PerfVersion2>>();
                builder.RegisterMigrator<PerfVersion3, PerfVersionTarget, PerfVersionMigrator<PerfVersion3>>();
                builder.RegisterMigrator<PerfVersion4, PerfVersionTarget, PerfVersionMigrator<PerfVersion4>>();
            }

            if (Position.SourceCount >= 8)
            {
                builder.RegisterMigrator<PerfVersion5, PerfVersionTarget, PerfVersionMigrator<PerfVersion5>>();
                builder.RegisterMigrator<PerfVersion6, PerfVersionTarget, PerfVersionMigrator<PerfVersion6>>();
                builder.RegisterMigrator<PerfVersion7, PerfVersionTarget, PerfVersionMigrator<PerfVersion7>>();
                builder.RegisterMigrator<PerfVersion8, PerfVersionTarget, PerfVersionMigrator<PerfVersion8>>();
            }
        });

        payload = Encoding.UTF8.GetBytes($"{{\"$type\":\"{Discriminator(Position.Selected.ToString(System.Globalization.CultureInfo.InvariantCulture))}\",\"value\":42}}");
        var result = Migrate();
        if (result.Value != 42 || result.Version != Position.Selected)
        {
            throw new InvalidOperationException("Version benchmark selected the wrong migration.");
        }
    }

    [Benchmark]
    public PerfVersionTarget Migrate() => JsonSerializer.Deserialize<PerfVersionTarget>(payload, options)!;

    private string Discriminator(string version) => Layout is DiscriminatorLayout.SharedPrefix
        ? $"version-{version}"
        : new string('v', version is "current" ? 9 : int.Parse(version, System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>The version number a payload selects, out of how many registered sources.</summary>
public readonly record struct VersionPosition(int Selected, int SourceCount)
{
    public override string ToString() => $"{Selected}/{SourceCount}";
}

public enum DiscriminatorLayout { SharedPrefix, DifferentLengths }

internal static class HotPathOptions
{
    public static JsonSerializerOptions Create(bool sourceGeneration) => sourceGeneration
        ? new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = HotPathJsonContext.Default }
        : new JsonSerializerOptions(JsonSerializerDefaults.Web);
}

[JsonMigratable(TypeDiscriminator = "struct-v1")]
public readonly record struct PerfStructSource(int Value);

[JsonMigratable(TypeDiscriminator = "struct-v2")]
public readonly record struct PerfStructStaticTarget(int Value) : IMigrateFrom<PerfStructSource, PerfStructStaticTarget>
{
    public static bool TryMigrateFrom(PerfStructSource source, out PerfStructStaticTarget result)
    {
        result = new(source.Value + 1);
        return true;
    }
}

[JsonMigratable(TypeDiscriminator = "struct-v2")]
public readonly record struct PerfStructExternalTarget(int Value);

public sealed class PerfStructMigrator : IMigrate<PerfStructSource, PerfStructExternalTarget>
{
    public bool TryMigrateFrom(PerfStructSource source, out PerfStructExternalTarget result)
    {
        result = new(source.Value + 1);
        return true;
    }
}

public interface IPerfVersion { int Value { get; } int Version { get; } }

[JsonMigratable(TypeDiscriminator = "current")]
public sealed record PerfVersionTarget(int Value, int Version);

public sealed class PerfVersionMigrator<TSource> : IMigrate<TSource, PerfVersionTarget>
    where TSource : IPerfVersion
{
    public bool TryMigrateFrom(TSource source, out PerfVersionTarget result)
    {
        result = new(source.Value, source.Version);
        return true;
    }
}

[JsonMigratable(TypeDiscriminator = "1")]
public sealed record PerfVersion1(int Value) : IPerfVersion { public int Version => 1; }
[JsonMigratable(TypeDiscriminator = "2")]
public sealed record PerfVersion2(int Value) : IPerfVersion { public int Version => 2; }
[JsonMigratable(TypeDiscriminator = "3")]
public sealed record PerfVersion3(int Value) : IPerfVersion { public int Version => 3; }
[JsonMigratable(TypeDiscriminator = "4")]
public sealed record PerfVersion4(int Value) : IPerfVersion { public int Version => 4; }
[JsonMigratable(TypeDiscriminator = "5")]
public sealed record PerfVersion5(int Value) : IPerfVersion { public int Version => 5; }
[JsonMigratable(TypeDiscriminator = "6")]
public sealed record PerfVersion6(int Value) : IPerfVersion { public int Version => 6; }
[JsonMigratable(TypeDiscriminator = "7")]
public sealed record PerfVersion7(int Value) : IPerfVersion { public int Version => 7; }
[JsonMigratable(TypeDiscriminator = "8")]
public sealed record PerfVersion8(int Value) : IPerfVersion { public int Version => 8; }

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(PerfStructSource))]
[JsonSerializable(typeof(PerfStructStaticTarget))]
[JsonSerializable(typeof(PerfStructExternalTarget))]
[JsonSerializable(typeof(PerfVersionTarget))]
[JsonSerializable(typeof(PerfVersion1))]
[JsonSerializable(typeof(PerfVersion2))]
[JsonSerializable(typeof(PerfVersion3))]
[JsonSerializable(typeof(PerfVersion4))]
[JsonSerializable(typeof(PerfVersion5))]
[JsonSerializable(typeof(PerfVersion6))]
[JsonSerializable(typeof(PerfVersion7))]
[JsonSerializable(typeof(PerfVersion8))]
internal partial class HotPathJsonContext : JsonSerializerContext;
