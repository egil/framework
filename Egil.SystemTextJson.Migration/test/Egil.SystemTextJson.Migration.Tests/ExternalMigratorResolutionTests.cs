using System.Text.Json;

namespace Egil.SystemTextJson.Migration.Tests;

/// <summary>
/// How an external migrator instance is obtained for each migration: from the configured
/// <see cref="IServiceProvider"/> on every call, otherwise from a fallback instance that is
/// constructed once.
/// </summary>
public class ExternalMigratorResolutionTests
{
    [Fact]
    public void Provider_is_consulted_again_after_a_fallback_was_cached()
    {
        var provider = new SwitchingProvider();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddJsonMigrationSupport(provider,
            builder => builder.RegisterMigrator<StructMigrator>());

        var fallback = JsonSerializer.Deserialize<ExternalTarget>("""{"$type":"struct-source","value":41}""", options);
        provider.Migrator = new StructMigrator(10);
        var fromProvider = JsonSerializer.Deserialize<ExternalTarget>("""{"$type":"struct-source","value":41}""", options);
        provider.Migrator = null;
        var fallbackAgain = JsonSerializer.Deserialize<ExternalTarget>("""{"$type":"struct-source","value":41}""", options);

        Assert.Equal(42, fallback.Value);
        Assert.Equal(51, fromProvider.Value);
        Assert.Equal(42, fallbackAgain.Value);
    }

    [Fact]
    public async Task Fallback_is_constructed_once_when_shared_options_are_used_concurrently()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddJsonMigrationSupport(
            builder => builder.RegisterMigrator<IdentifiedMigrator<ConstructionProbe>>());
        options.MakeReadOnly();
        _ = options.GetTypeInfo(typeof(IdentifiedTarget));

        var results = await MigrateConcurrently(options, TestContext.Current.CancellationToken);

        Assert.Equal(1, IdentifiedMigrator<ConstructionProbe>.ConstructionCount);
        Assert.Single(results.Select(result => result.InstanceId).Distinct());
        Assert.All(results, result => Assert.Equal(42, result.Value));
    }

    [Fact]
    public void Fallback_constructor_failure_is_deferred_until_migration_and_cached()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddJsonMigrationSupport(
            builder => builder.RegisterMigrator<ThrowingMigrator<int>>());
        var current = JsonSerializer.Deserialize<ExternalTarget>("""{"$type":"external-target","value":42}""", options);

        var first = Assert.Throws<InvalidOperationException>(() => JsonSerializer.Deserialize<ExternalTarget>(
            """{"$type":"struct-source","value":41}""", options));
        var second = Assert.Throws<InvalidOperationException>(() => JsonSerializer.Deserialize<ExternalTarget>(
            """{"$type":"struct-source","value":41}""", options));

        Assert.Equal(42, current.Value);
        Assert.Same(first, second);
        Assert.Contains("could not be created", first.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_provider_overload_invokes_registration_callback()
    {
        var options = new JsonSerializerOptions().AddJsonMigrationSupport(new EmptyProvider(), b => b.RegisterMigrator<ChainExternalMigrator>());

        var result = JsonSerializer.Deserialize<ChainV3>("""{"$type":"chain-v1","Name":"Jane Doe"}""", options);

        Assert.Equal(new ChainV3("Jane Doe"), result);
    }

    [Fact]
    public void Service_provider_overload_without_callback_supports_static_migration()
    {
        var options = new JsonSerializerOptions().AddJsonMigrationSupport(new EmptyProvider());

        var result = JsonSerializer.Deserialize<ChainV2>("""{"$type":"chain-v1","Name":"Jane Doe"}""", options);

        Assert.Equal(new ChainV2("Jane", "Doe"), result);
    }

    private static async Task<IdentifiedTarget[]> MigrateConcurrently(JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        // Dedicated threads meet before the first migration so the thread pool and
        // converter initialization cannot turn the cold fallback test into serial calls.
        using var start = new Barrier(8);
        var calls = Enumerable.Range(0, 8).Select(_ => Task.Factory.StartNew(() =>
        {
            start.SignalAndWait(cancellationToken);
            return JsonSerializer.Deserialize<IdentifiedTarget>("""{"$type":"struct-source","value":41}""", options)!;
        }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        return await Task.WhenAll(calls);
    }

    [JsonMigratable(TypeDiscriminator = "struct-source")]
    public readonly record struct StructSource(int Value);

    [JsonMigratable(TypeDiscriminator = "external-target")]
    public readonly record struct ExternalTarget(int Value);

    public sealed class StructMigrator : IMigrate<StructSource, ExternalTarget>
    {
        private readonly int increment;

        public StructMigrator() : this(1) { }

        public StructMigrator(int increment) => this.increment = increment;

        public bool TryMigrateFrom(StructSource source, out ExternalTarget result)
        {
            result = new(source.Value + increment);
            return true;
        }
    }

    private sealed class SwitchingProvider : IServiceProvider
    {
        public StructMigrator? Migrator { get; set; }

        public object? GetService(Type serviceType) => serviceType == typeof(StructMigrator) ? Migrator : null;
    }

    private sealed class EmptyProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    [JsonMigratable]
    public sealed record IdentifiedTarget(int Value, Guid InstanceId);

    private sealed class ConstructionProbe;

    public sealed class IdentifiedMigrator<TMarker> : IMigrate<StructSource, IdentifiedTarget>
    {
        private static int constructionCount;
        private readonly Guid instanceId = Guid.NewGuid();

        public IdentifiedMigrator() => Interlocked.Increment(ref constructionCount);

        public static int ConstructionCount => Volatile.Read(ref constructionCount);

        public bool TryMigrateFrom(StructSource source, out IdentifiedTarget result)
        {
            result = new(source.Value + 1, instanceId);
            return true;
        }
    }

    // Generic so assembly scans skip it: RegisterMigratorsFromAssembly registers only closed
    // types, and this migrator shares its source and target with StructMigrator.
    public sealed class ThrowingMigrator<TMarker> : IMigrate<StructSource, ExternalTarget>
    {
        public ThrowingMigrator() => throw new InvalidOperationException("Constructor failed.");

        public bool TryMigrateFrom(StructSource source, out ExternalTarget result) => throw new NotSupportedException();
    }
}
