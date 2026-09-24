using Microsoft.CodeAnalysis;

namespace Egil.SystemTextJson.Migration.Analyzers;

internal sealed class MigrationSymbols
{
    private MigrationSymbols(INamedTypeSymbol migrate, INamedTypeSymbol migrateFrom, INamedTypeSymbol jsonMigratableAttribute)
    {
        Migrate = migrate;
        MigrateFrom = migrateFrom;
        JsonMigratableAttribute = jsonMigratableAttribute;
    }

    public INamedTypeSymbol Migrate { get; }

    public INamedTypeSymbol MigrateFrom { get; }

    public INamedTypeSymbol JsonMigratableAttribute { get; }

    public static bool TryCreate(Compilation compilation, out MigrationSymbols? symbols)
    {
        var migrate = compilation.GetTypeByMetadataName("Egil.SystemTextJson.Migration.IMigrate`2");
        var migrateFrom = compilation.GetTypeByMetadataName("Egil.SystemTextJson.Migration.IMigrateFrom`2");
        var jsonMigratableAttribute = compilation.GetTypeByMetadataName("Egil.SystemTextJson.Migration.JsonMigratableAttribute");

        if (migrate is null || migrateFrom is null || jsonMigratableAttribute is null)
        {
            symbols = null;
            return false;
        }

        symbols = new MigrationSymbols(migrate, migrateFrom, jsonMigratableAttribute);
        return true;
    }
}
