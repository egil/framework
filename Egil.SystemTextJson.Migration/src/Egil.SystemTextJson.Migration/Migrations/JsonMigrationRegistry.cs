using System.Collections.Frozen;

namespace Egil.SystemTextJson.Migration.Migrations;

internal sealed class JsonMigrationRegistry(FrozenDictionary<Type, FrozenDictionary<Type, ExternalMigratorRegistration>> registrationsByTarget)
{
    private readonly FrozenDictionary<Type, FrozenDictionary<Type, ExternalMigratorRegistration>> registrationsByTarget = registrationsByTarget;

    public IEnumerable<ExternalMigratorRegistration> GetForTarget(Type targetType)
        => registrationsByTarget.TryGetValue(targetType, out FrozenDictionary<Type, ExternalMigratorRegistration>? registrations)
            ? registrations.Values
            : [];
}
