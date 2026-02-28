using System.Collections.Frozen;
using System.Reflection;

namespace Egil.SystemTextJson.Migration.Migrations;

internal sealed class JsonMigrationRegistry(
    FrozenDictionary<Type, FrozenDictionary<Type, ExternalMigratorRegistration>> registrationsByTarget,
    Func<Type, string?>? typeDiscriminatorResolver,
    string? defaultDiscriminatorPropertyName,
    JsonMigrationFailureHandling migrationFailureHandling)
{
    private readonly FrozenDictionary<Type, FrozenDictionary<Type, ExternalMigratorRegistration>> registrationsByTarget = registrationsByTarget;
    private readonly Func<Type, string?>? typeDiscriminatorResolver = typeDiscriminatorResolver;
    private readonly string? defaultDiscriminatorPropertyName = defaultDiscriminatorPropertyName;

    public IEnumerable<ExternalMigratorRegistration> GetForTarget(Type targetType)
        => registrationsByTarget.TryGetValue(targetType, out FrozenDictionary<Type, ExternalMigratorRegistration>? registrations)
            ? registrations.Values
            : [];

    public TypeMetadata GetTypeMetadata(Type type)
        => TypeMetadata.FromType(type, typeDiscriminatorResolver, defaultDiscriminatorPropertyName);

    public JsonMigrationFailureHandling GetMigrationFailureHandling(Type targetType)
    {
        if (!TryGetConfiguredMigrationFailureHandling(targetType, out JsonMigrationFailureHandling configuredHandling))
        {
            return migrationFailureHandling;
        }

        return configuredHandling;
    }

    private static bool TryGetConfiguredMigrationFailureHandling(Type targetType, out JsonMigrationFailureHandling handling)
    {
        for (Type? current = targetType; current is not null; current = current.BaseType)
        {
            CustomAttributeData? attributeData = current
                .GetCustomAttributesData()
                .FirstOrDefault(static attribute => attribute.AttributeType == typeof(JsonMigratableAttribute));

            if (attributeData is null)
            {
                continue;
            }

            foreach (CustomAttributeNamedArgument namedArgument in attributeData.NamedArguments)
            {
                if (namedArgument.MemberName != nameof(JsonMigratableAttribute.MigrationFailureHandling))
                {
                    continue;
                }

                handling = (JsonMigrationFailureHandling)namedArgument.TypedValue.Value!;
                return true;
            }

            // The nearest attribute exists but did not explicitly configure the policy.
            break;
        }

        handling = default;
        return false;
    }
}
