# Dependency Injection

## Using DI for external migrators

Pass an `IServiceProvider` so that external migrators can have constructor-injected dependencies:

<!-- snippet: scoped_migrator_type -->
<a id='snippet-scoped_migrator_type'></a>
```cs
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
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/ScopedMigratorsSample.cs#L11-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-scoped_migrator_type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: scoped_migrator_usage -->
<a id='snippet-scoped_migrator_usage'></a>
```cs
var services = new ServiceCollection();
services.AddSingleton<IUserLookupService, FakeUserLookup>();
services.AddTransient<AuditMigrator>();
var serviceProvider = services.BuildServiceProvider();

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport(serviceProvider, static builder =>
    builder.RegisterMigrator<AuditEntryV1, AuditEntryV2, AuditMigrator>());

var json = """{"$type":"audit-v1","action":"login","user":"Jane Doe"}""";
var entry = JsonSerializer.Deserialize<AuditEntryV2>(json, options);
// entry.UserId resolved via IUserLookupService from DI
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/ScopedMigratorsSample.cs#L45-L58' title='Snippet source file'>snippet source</a> | <a href='#snippet-scoped_migrator_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

There's also a convenience overload that accepts the service provider directly:

<!-- snippet: di_migrators -->
<a id='snippet-di_migrators'></a>
```cs
var services = new ServiceCollection();
services.AddScoped<UserMigrator>();

using var serviceProvider = services.BuildServiceProvider();

// When building serializer options, pass the service provider:
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport(serviceProvider, builder =>
{
    builder.RegisterMigrator<UserMigrator>();
});
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/DiMigratorsSample.cs#L26-L38' title='Snippet source file'>snippet source</a> | <a href='#snippet-di_migrators' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** If the service provider returns `null` for the migrator type, the library falls back to creating the migrator via its parameterless constructor. If neither works, an `InvalidOperationException` is thrown.

## Scoped migrators

The service provider is queried for the migrator on **each migration call**, not once at setup time. This means migrators respect the lifetime registered in DI:

<!-- snippet: scoped_migrator_per_call -->
<a id='snippet-scoped_migrator_per_call'></a>
```cs
var services = new ServiceCollection();
services.AddSingleton<IUserLookupService, FakeUserLookup>();
// Transient: a new AuditMigrator is created for each migration call.
// Use Scoped in ASP.NET Core to get per-request behavior.
services.AddTransient<AuditMigrator>();
var serviceProvider = services.BuildServiceProvider();

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport(serviceProvider, static builder =>
    builder.RegisterMigrator<AuditEntryV1, AuditEntryV2, AuditMigrator>());

var json = """{"$type":"audit-v1","action":"login","user":"Jane Doe"}""";

// The service provider is queried for the migrator on EACH call.
var first = JsonSerializer.Deserialize<AuditEntryV2>(json, options);
var second = JsonSerializer.Deserialize<AuditEntryV2>(json, options);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/ScopedMigratorsSample.cs#L69-L86' title='Snippet source file'>snippet source</a> | <a href='#snippet-scoped_migrator_per_call' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** In ASP.NET Core, register migrators as `Scoped` to get per-request behavior. The `IServiceProvider` passed to `AddJsonMigrationSupport()` should be scoped appropriately for your use case.
