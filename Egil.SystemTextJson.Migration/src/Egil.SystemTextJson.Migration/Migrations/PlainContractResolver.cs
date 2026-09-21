using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Migrations;

/// <summary>
/// The resolver of an exclusion clone: the options used while one migratable type's converter is
/// being built. It forwards every request to the chain the clone inherited, and for the type being
/// built adds the discriminator property to the plain object contract that comes back.
/// </summary>
/// <remarks>
/// The migration resolver inside the inherited chain reads the clone's <see cref="MigrationScope"/>
/// and, for the excluded type, either steps aside so the next resolver in the chain supplies the
/// contract or, when it stands in for the default resolver, returns the reflection contract itself.
/// Either way the contract travels back through whatever decorates the chain before it is
/// modified here, and wrapping the inherited resolver instead of editing the chain works when the
/// migration resolver is not directly visible in it.
/// </remarks>
internal sealed class PlainContractResolver(IJsonTypeInfoResolver inner, MigrationScope scope) : IJsonTypeInfoResolver
{
    public IJsonTypeInfoResolver Inner { get; } = inner;

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        JsonTypeInfo? typeInfo = Inner.GetTypeInfo(type, options);

        if (!scope.IsBuildingConverterFor(type))
        {
            return typeInfo;
        }

        if (typeInfo is not null)
        {
            JsonMigrationTypeInfoResolver.AddDiscriminatorProperty(typeInfo, scope.Registry.GetTypeMetadata(type));
        }

        return typeInfo;
    }
}
