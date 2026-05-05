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
        string? customDiscriminator = typeDiscriminatorResolver?.Invoke(type);
        string discriminator = customDiscriminator ?? attribute?.TypeDiscriminator ?? type.FullName ?? type.Name;
        string propertyName = attribute?.TypeDiscriminatorPropertyName ?? defaultDiscriminatorPropertyName ?? "$type";
        return new TypeMetadata(type, discriminator, propertyName, attribute?.UndiscriminatedSourceType);
    }
}
