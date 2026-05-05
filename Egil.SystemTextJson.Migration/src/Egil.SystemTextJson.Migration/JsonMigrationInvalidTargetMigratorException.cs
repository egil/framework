using System.Text.Json;

namespace Egil.SystemTextJson.Migration;

/// <summary>
/// Thrown when a JSON migratable type implements the external migrator contract directly.
/// </summary>
public sealed class JsonMigrationInvalidTargetMigratorException : JsonException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonMigrationInvalidTargetMigratorException"/> class.
    /// </summary>
    public JsonMigrationInvalidTargetMigratorException(Type targetType, Type migratorContractType)
        : base(CreateMessage(targetType, migratorContractType))
    {
        TargetType = targetType;
        MigratorContractType = migratorContractType;
    }

    /// <summary>
    /// Gets the JSON migratable type with the invalid migrator contract.
    /// </summary>
    public Type TargetType { get; }

    /// <summary>
    /// Gets the invalid <see cref="IMigrate{TSource,TTarget}"/> contract implemented by <see cref="TargetType"/>.
    /// </summary>
    public Type MigratorContractType { get; }

    private static string CreateMessage(Type targetType, Type migratorContractType)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(migratorContractType);

        return
            $"JSON migratable type '{targetType.FullName}' implements external migrator contract '{migratorContractType.FullName}', which is not supported. " +
            $"Types marked with '{nameof(JsonMigratableAttribute)}' must use 'IMigrateFrom<TSource,TTarget>' for target-owned migrations; those contracts are discovered automatically when migration support creates the converter. " +
            "Use 'IMigrate<TSource,TTarget>' only on separate external migrator classes, and register those classes with RegisterMigrator*, RegisterMigrator<TMigrator>(), RegisterMigratorsFromAssembly, or RegisterMigratorsFromAssemblies. " +
            "See the 'Choosing a migration contract' section in the Egil.SystemTextJson.Migration README: https://github.com/egil/framework/tree/main/Egil.SystemTextJson.Migration#choosing-a-migration-contract";
    }
}
