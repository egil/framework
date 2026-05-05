using System.Reflection;

namespace Egil.SystemTextJson.Migration.Migrations;

internal sealed record TypeMetadata(
    Type Type,
    string Discriminator,
    string DiscriminatorPropertyName,
    Type? UndiscriminatedSourceType)
{
    public static TypeMetadata FromType(
        Type type,
        Func<Type, string?>? typeDiscriminatorResolver = null,
        string? defaultDiscriminatorPropertyName = null)
    {
        JsonMigratableAttribute? attribute = type.GetCustomAttribute<JsonMigratableAttribute>(inherit: true);
        JsonMigratableAttribute? declaredAttribute = type.GetCustomAttribute<JsonMigratableAttribute>(inherit: false);
        string? customDiscriminator = typeDiscriminatorResolver?.Invoke(type);
        string discriminator = customDiscriminator ?? declaredAttribute?.TypeDiscriminator ?? type.FullName ?? type.Name;
        string propertyName = attribute?.TypeDiscriminatorPropertyName ?? defaultDiscriminatorPropertyName ?? "$type";
        return new TypeMetadata(type, discriminator, propertyName, attribute?.UndiscriminatedSourceType);
    }
}
