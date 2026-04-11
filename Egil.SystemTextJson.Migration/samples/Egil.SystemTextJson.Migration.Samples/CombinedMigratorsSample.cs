namespace Egil.SystemTextJson.Migration.Samples.CombinedMigrators;

[JsonMigratable(TypeDiscriminator = "contact-v1")]
public record class ContactV1(string FullName, string Email);

#region combined_migrators_static_wins
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
#endregion

public sealed class CombinedMigratorsSampleTests
{
    [Fact]
    public void Static_migrator_wins_over_external()
    {
        #region combined_migrators_usage
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport(static builder =>
        {
            // Even though we register an external migrator...
            builder.RegisterMigrator<ContactExternalMigrator>();
            // ...the static IMigrateFrom<,> on ContactV2 takes precedence.
        });

        var json = """{"$type":"contact-v1","fullName":"Egil Hansen","email":"egil@example.com"}""";
        var contact = JsonSerializer.Deserialize<ContactV2>(json, options);
        // contact.FirstName == "Egil" (from static migrator, not "External")
        #endregion

        Assert.NotNull(contact);
        Assert.Equal("Egil", contact.FirstName);
        Assert.Equal("Hansen", contact.LastName);
    }
}
