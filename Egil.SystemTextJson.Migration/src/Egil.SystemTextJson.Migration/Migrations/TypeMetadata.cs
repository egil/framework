using System.Reflection;

namespace Egil.SystemTextJson.Migration.Migrations;

internal sealed record TypeMetadata(
    Type Type,
    string Discriminator,
    string DiscriminatorPropertyName)
{
    public static TypeMetadata FromType(Type type)
    {
        JsonMigratableAttribute? attribute = type.GetCustomAttribute<JsonMigratableAttribute>(inherit: true);
        string discriminator = attribute?.TypeDiscriminator ?? type.FullName ?? type.Name;
        string propertyName = attribute?.TypeDiscriminatorPropertyName ?? "$type";
        return new TypeMetadata(type, discriminator, propertyName);
    }
}
