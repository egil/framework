using Microsoft.Extensions.DependencyInjection;

namespace Egil.Orleans.StateMigration.Tests;

public sealed class StateMigrationRegistrationTests
{
    [Fact]
    public void Add_state_migrator_registers_external_migrator_for_resolution()
    {
        var services = new ServiceCollection();
        services
            .AddStateMigrator<SourceState, TargetState, SourceToTargetMigrator>()
            .AddStateMigration();

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        var resolver = serviceProvider.GetRequiredService<IMigrationResolver>();

        TargetState migrated = resolver.Migrate<SourceState, TargetState>(new("alice"));

        Assert.Equal("migrated:alice", migrated.Name);
    }

    [Fact]
    public void Add_state_migration_fails_for_open_generic_migrator_registration()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(IMigrate<,>), typeof(OpenGenericMigrator<,>));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => services.AddStateMigration());

        Assert.Contains("closed generic", exception.Message, StringComparison.Ordinal);
    }
    private sealed record SourceState(string Name);

    private sealed record TargetState(string Name);

    private sealed class SourceToTargetMigrator : IMigrate<SourceState, TargetState>
    {
        public TargetState Migrate(SourceState source)
            => new($"migrated:{source.Name}");
    }

    private sealed class OpenGenericMigrator<TSource, TTarget> : IMigrate<TSource, TTarget>
    {
        public TTarget Migrate(TSource source)
            => throw new NotSupportedException("Open generic migrator should never be executed.");
    }
}
