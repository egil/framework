# Registration & Discovery

## Registering a single migrator explicitly

Register individual external migrators using `RegisterMigrator<T>()`:

<!-- snippet: external_migration_setup -->
<a id='snippet-external_migration_setup'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport(builder =>
{
    builder.RegisterMigrator<UserMigrator>();
});
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/ExternalMigrationSample.cs#L26-L32' title='Snippet source file'>snippet source</a> | <a href='#snippet-external_migration_setup' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can also use the explicit three-type-parameter form when you want to be more specific:

```csharp
builder.RegisterMigrator<UserV1, UserV2, UserExternalMigrator>();
```

> **Note:** Static migrators (`IMigrateFrom`) are discovered automatically from the target type's interfaces — no registration needed.

## Assembly scanning

Automatically discover all `IMigrate<,>` implementations in one or more assemblies:

<!-- snippet: assembly_scanning -->
<a id='snippet-assembly_scanning'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport(builder =>
{
    builder.RegisterMigratorsFromAssemblies(typeof(UserMigrator).Assembly);
});
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/AssemblyScanningSample.cs#L24-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-assembly_scanning' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** Assembly scanning only discovers external migrators (`IMigrate<,>`). Static migrators (`IMigrateFrom<,>`) are always discovered automatically from the target type.

## Combining static and external migrators

When both a static migrator (`IMigrateFrom`) and an external migrator (`IMigrate`) exist for the same source→target pair, the **static migrator always wins**.

<!-- snippet: combined_migrators_static_wins -->
<a id='snippet-combined_migrators_static_wins'></a>
```cs
[JsonMigratable(TypeDiscriminator = "contact-v2")]
public record class ContactV2(string FirstName, string LastName, string Email)
    : IMigrateFrom<ContactV1, ContactV2>
{
    // This static migrator takes precedence over any registered
    // external migrator for ContactV1 → ContactV2.
    public static bool TryMigrateFrom(ContactV1 source, out ContactV2 result)
    {
        var names = source.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        result = new ContactV2(
            names.Length > 0 ? names[0] : string.Empty,
            names.Length > 1 ? names[1] : string.Empty,
            source.Email);
        return true;
    }
}

public class ContactExternalMigrator : IMigrate<ContactV1, ContactV2>
{
    // This will NOT be called because the static migrator on ContactV2 wins.
    public bool TryMigrateFrom(ContactV1 source, out ContactV2 result)
    {
        result = new ContactV2("External", "Migrator", source.Email);
        return true;
    }
}
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/CombinedMigratorsSample.cs#L6-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-combined_migrators_static_wins' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: combined_migrators_usage -->
<a id='snippet-combined_migrators_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport(static builder =>
{
    // Even though we register an external migrator...
    builder.RegisterMigrator<ContactExternalMigrator>();
    // ...the static IMigrateFrom<,> on ContactV2 takes precedence.
});

var json = """{"$type":"contact-v1","fullName":"Jane Doe","email":"jane@example.com"}""";
var contact = JsonSerializer.Deserialize<ContactV2>(json, options);
// contact.FirstName == "Jane" (from static migrator, not "External")
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/CombinedMigratorsSample.cs#L40-L52' title='Snippet source file'>snippet source</a> | <a href='#snippet-combined_migrators_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** This precedence rule lets you provide a default external migrator (e.g., via assembly scanning) while allowing individual types to override with their own static migrator.
