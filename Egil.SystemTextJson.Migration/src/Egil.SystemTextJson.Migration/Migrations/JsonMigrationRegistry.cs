using System.Collections.Frozen;

namespace Egil.SystemTextJson.Migration.Migrations;

internal sealed class JsonMigrationRegistry(
    FrozenDictionary<Type, FrozenDictionary<Type, ExternalMigratorRegistration>> registrationsByTarget,
    Func<Type, string?>? typeDiscriminatorResolver)
{
    private readonly FrozenDictionary<Type, FrozenDictionary<Type, ExternalMigratorRegistration>> registrationsByTarget = registrationsByTarget;
    private readonly Func<Type, string?>? typeDiscriminatorResolver = typeDiscriminatorResolver;

    public IEnumerable<ExternalMigratorRegistration> GetForTarget(Type targetType)
        => registrationsByTarget.TryGetValue(targetType, out FrozenDictionary<Type, ExternalMigratorRegistration>? registrations)
            ? registrations.Values
            : [];

    public TypeMetadata GetTypeMetadata(Type type)
        => TypeMetadata.FromType(type, typeDiscriminatorResolver);
}
