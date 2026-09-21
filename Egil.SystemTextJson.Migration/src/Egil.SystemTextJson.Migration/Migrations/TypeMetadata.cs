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
        // A Nullable<T> source is read by T's converter and written with T's discriminator, so
        // its contract is T's; the declared type is kept for deserialization.
        Type contractType = Nullable.GetUnderlyingType(type) ?? type;
        JsonMigratableAttribute? attribute = contractType.GetCustomAttribute<JsonMigratableAttribute>(inherit: true);
        JsonMigratableAttribute? declaredAttribute = contractType.GetCustomAttribute<JsonMigratableAttribute>(inherit: false);
        string? customDiscriminator = typeDiscriminatorResolver?.Invoke(contractType);
        string discriminator = customDiscriminator ?? declaredAttribute?.TypeDiscriminator ?? contractType.FullName ?? contractType.Name;
        string propertyName = attribute?.TypeDiscriminatorPropertyName ?? defaultDiscriminatorPropertyName ?? "$type";
        return new TypeMetadata(type, discriminator, propertyName, attribute?.UndiscriminatedSourceType);
    }
}
