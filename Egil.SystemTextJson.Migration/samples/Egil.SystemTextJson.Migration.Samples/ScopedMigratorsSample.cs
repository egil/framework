using Microsoft.Extensions.DependencyInjection;

namespace Egil.SystemTextJson.Migration.Samples.ScopedMigrators;

[JsonMigratable(TypeDiscriminator = "audit-v1")]
public record class AuditEntryV1(string Action, string User);

[JsonMigratable(TypeDiscriminator = "audit-v2")]
public record class AuditEntryV2(string Action, string UserId, string MigratedBy);

#region scoped_migrator_type
public sealed class AuditMigrator : IMigrate<AuditEntryV1, AuditEntryV2>
{
    private readonly IUserLookupService userLookup;

    public AuditMigrator(IUserLookupService userLookup)
    {
        this.userLookup = userLookup;
    }

    public bool TryMigrateFrom(AuditEntryV1 source, out AuditEntryV2 result)
    {
        var userId = userLookup.GetUserId(source.User);
        result = new AuditEntryV2(source.Action, userId, "migration-service");
        return true;
    }
}

public interface IUserLookupService
{
    string GetUserId(string userName);
}
#endregion

internal sealed class FakeUserLookup : IUserLookupService
{
    public string GetUserId(string userName) => $"uid-{userName.ToLowerInvariant().Replace(' ', '-')}";
}

public sealed class ScopedMigratorsSampleTests
{
    [Fact]
    public void Scoped_migrator_resolved_from_service_provider()
    {
        #region scoped_migrator_usage
        var services = new ServiceCollection();
        services.AddSingleton<IUserLookupService, FakeUserLookup>();
        services.AddTransient<AuditMigrator>();
        var serviceProvider = services.BuildServiceProvider();

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(serviceProvider, static builder =>
            builder.RegisterMigrator<AuditEntryV1, AuditEntryV2, AuditMigrator>());

        var json = """{"$type":"audit-v1","action":"login","user":"Egil Hansen"}""";
        var entry = JsonSerializer.Deserialize<AuditEntryV2>(json, options);
        // entry.UserId resolved via IUserLookupService from DI
        #endregion

        Assert.NotNull(entry);
        Assert.Equal("login", entry.Action);
        Assert.Equal("uid-egil-hansen", entry.UserId);
        Assert.Equal("migration-service", entry.MigratedBy);
    }

    [Fact]
    public void Each_migration_gets_fresh_migrator_instance()
    {
        #region scoped_migrator_per_call
        var services = new ServiceCollection();
        services.AddSingleton<IUserLookupService, FakeUserLookup>();
        // Transient: a new AuditMigrator is created for each migration call.
        // Use Scoped in ASP.NET Core to get per-request behavior.
        services.AddTransient<AuditMigrator>();
        var serviceProvider = services.BuildServiceProvider();

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(serviceProvider, static builder =>
            builder.RegisterMigrator<AuditEntryV1, AuditEntryV2, AuditMigrator>());

        var json = """{"$type":"audit-v1","action":"login","user":"Egil Hansen"}""";

        // The service provider is queried for the migrator on EACH call.
        var first = JsonSerializer.Deserialize<AuditEntryV2>(json, options);
        var second = JsonSerializer.Deserialize<AuditEntryV2>(json, options);
        #endregion

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("uid-egil-hansen", first.UserId);
        Assert.Equal("uid-egil-hansen", second.UserId);
    }
}
