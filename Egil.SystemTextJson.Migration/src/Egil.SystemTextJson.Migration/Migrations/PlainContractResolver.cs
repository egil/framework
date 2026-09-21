using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Migrations;

/// <summary>
/// The resolver of an exclusion clone: the options used while one migratable type's converter is
/// being built. It forwards every request to the chain the clone inherited, and for the type being
/// built adds the discriminator property to the plain object contract that comes back.
/// </summary>
/// <remarks>
/// The migration resolver inside the inherited chain steps aside for the excluded type (it reads the
/// clone's <see cref="MigrationScope"/>), so the contract comes from the next resolver in the chain,
/// or from the reflection fallback when the user configured none. Wrapping the inherited resolver
/// instead of editing the chain keeps any decorator the user applied, and works when the migration
/// resolver is not directly visible in the chain.
/// </remarks>
internal sealed class PlainContractResolver(IJsonTypeInfoResolver inner, MigrationScope scope) : IJsonTypeInfoResolver
{
    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        JsonTypeInfo? typeInfo = inner.GetTypeInfo(type, options);

        if (!scope.IsBuildingConverterFor(type))
        {
            return typeInfo;
        }

        typeInfo ??= scope.Resolver.ResolveReflectionFallback(type, options, scope);
        if (typeInfo is not null)
        {
            JsonMigrationTypeInfoResolver.AddDiscriminatorProperty(typeInfo, scope.Registry.GetTypeMetadata(type));
        }

        return typeInfo;
    }
}
