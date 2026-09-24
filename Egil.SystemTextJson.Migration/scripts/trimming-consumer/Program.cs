using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Egil.SystemTextJson.Migration;

// verify-trimming-package.ps1 reads diagnostic markers to assert real source locations.
// Keep each marker immediately above its call or declaration, with no intervening lines.
Calls.Setup();
Calls.Provider();
Calls.Generated();
Calls.RegisteredGenerated();
Calls.Register();
Calls.Configure();
#if NET11_0_OR_GREATER
Calls.Union();
#endif

static class Calls
{
    public static void Setup()
    {
        // diagnostic: Setup
        new JsonSerializerOptions().AddJsonMigrationSupport();
    }
    public static void Provider()
    {
        // diagnostic: Provider
        new JsonSerializerOptions().AddJsonMigrationSupport(new Services());
    }
    public static void Generated()
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = Context.Default };
        // diagnostic: Generated
        options.AddJsonMigrationSupport();
        // diagnostic: GeneratedProvider
        new JsonSerializerOptions { TypeInfoResolver = Context.Default }.AddJsonMigrationSupport(new Services());
        var result = JsonSerializer.Deserialize("{\"$type\":\"old\",\"Value\":42}", options.GetTypeInfo(typeof(Current))) as Current;
        if (result?.Value != 42)
        {
            throw new Exception("Old payload migration failed.");
        }
    }

    public static void RegisteredGenerated()
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = Context.Default };
        // diagnostic: RegisteredGenerated
        options.AddJsonMigrationSupport(new Services(), builder =>
        {
            // diagnostic: GeneratedGeneric
            builder.RegisterMigrator<Old, External, ExternalMigrator>();
        });
        var result = JsonSerializer.Deserialize("{\"$type\":\"old\",\"Value\":42}", options.GetTypeInfo(typeof(External))) as External;
        if (result?.Value != 43)
        {
            throw new Exception("Service-provider migration with generated metadata failed.");
        }
    }
    public static void Register()
    {
        // diagnostic: Generic
        new JsonMigrationBuilder().RegisterMigrator<Old, Current, Migrator>();
        // diagnostic: Discovered
        new JsonMigrationBuilder().RegisterMigrator<Migrator>();
        // diagnostic: Assembly
        new JsonMigrationBuilder().RegisterMigratorsFromAssembly(typeof(Migrator).Assembly);
        // diagnostic: Assemblies
        new JsonMigrationBuilder().RegisterMigratorsFromAssemblies(typeof(Migrator).Assembly);
    }

    public static void Configure()
    {
        // Configuration alone performs no discovery and must not acquire blanket Requires* warnings.
        new JsonMigrationBuilder()
            .UseServiceProvider(new Services())
            .GetTypeDiscriminatorFrom<JsonMigratableAttribute>(attribute => attribute.TypeDiscriminator)
            .SetTypeDiscriminatorPropertyName("$type")
            .SetMigrationFailureHandling(JsonMigrationFailureHandling.ThrowJsonException);
    }
#if NET11_0_OR_GREATER
    public static void Union()
    {
        // diagnostic: Classifier
        _ = new JsonMigratableUnionTypeClassifier();
        var options = new JsonSerializerOptions { TypeInfoResolver = Context.Default };
        // diagnostic: UnionSetup
        options.AddJsonMigrationSupport();
        _ = JsonSerializer.Deserialize("{\"$type\":\"old\",\"Value\":42}", options.GetTypeInfo(typeof(Result)));
    }
#endif
}
sealed class Services : IServiceProvider
{
    public object? GetService(Type type) => type == typeof(ExternalMigrator) ? new ExternalMigrator(1) : null;
}
[JsonMigratable(TypeDiscriminator = "old")]
public record Old(int Value);
[JsonMigratable(TypeDiscriminator = "current")]
public record Current(int Value) : IMigrateFrom<Old, Current>
{
    public static bool TryMigrateFrom(Old source, out Current result)
    {
        result = new(source.Value);
        return true;
    }
}
public sealed class Migrator : IMigrate<Old, Current>
{
    public bool TryMigrateFrom(Old source, out Current result)
    {
        result = new(source.Value);
        return true;
    }
}

[JsonMigratable(TypeDiscriminator = "external")]
public record External(int Value);

public sealed class ExternalMigrator(int increment) : IMigrate<Old, External>
{
    public bool TryMigrateFrom(Old source, out External result)
    {
        result = new(source.Value + increment);
        return true;
    }
}
#if NET11_0_OR_GREATER
// diagnostic: UnionAttribute
[JsonUnion(TypeClassifier = typeof(JsonMigratableUnionTypeClassifier))]
public union Result(Current, string);
[JsonSerializable(typeof(Result))]
#endif
[JsonSerializable(typeof(Old))]
[JsonSerializable(typeof(Current))]
[JsonSerializable(typeof(External))]
[JsonSerializable(typeof(string))]
internal partial class Context : JsonSerializerContext;
