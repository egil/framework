using Microsoft.CodeAnalysis;

namespace Egil.SystemTextJson.Migration.Analyzers;

internal sealed class MigrationSymbols
{
    private MigrationSymbols(
        INamedTypeSymbol migrate,
        INamedTypeSymbol migrateFrom,
        INamedTypeSymbol jsonMigratableAttribute,
        INamedTypeSymbol jsonPolymorphicAttribute,
        INamedTypeSymbol? jsonDerivedTypeAttribute)
    {
        Migrate = migrate;
        MigrateFrom = migrateFrom;
        JsonMigratableAttribute = jsonMigratableAttribute;
        JsonPolymorphicAttribute = jsonPolymorphicAttribute;
        JsonDerivedTypeAttribute = jsonDerivedTypeAttribute;
    }

    public INamedTypeSymbol Migrate { get; }

    public INamedTypeSymbol MigrateFrom { get; }

    public INamedTypeSymbol JsonMigratableAttribute { get; }

    public INamedTypeSymbol JsonPolymorphicAttribute { get; }

    public INamedTypeSymbol? JsonDerivedTypeAttribute { get; }

    public static bool TryCreate(Compilation compilation, out MigrationSymbols? symbols)
    {
        var migrate = compilation.GetTypeByMetadataName("Egil.SystemTextJson.Migration.IMigrate`2");
        var migrateFrom = compilation.GetTypeByMetadataName("Egil.SystemTextJson.Migration.IMigrateFrom`2");
        var jsonMigratableAttribute = compilation.GetTypeByMetadataName("Egil.SystemTextJson.Migration.JsonMigratableAttribute");
        var jsonPolymorphicAttribute = compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonPolymorphicAttribute");
        var jsonDerivedTypeAttribute = compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonDerivedTypeAttribute");

        if (migrate is null || migrateFrom is null || jsonMigratableAttribute is null || jsonPolymorphicAttribute is null)
        {
            symbols = null;
            return false;
        }

        symbols = new MigrationSymbols(migrate, migrateFrom, jsonMigratableAttribute, jsonPolymorphicAttribute, jsonDerivedTypeAttribute);
        return true;
    }
}
