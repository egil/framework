using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.SystemTextJson.Migration.Migrations;

internal static class JsonMigratableTypes
{
    /// <summary>
    /// Returns whether <paramref name="type"/> (or a base type) is annotated with <see cref="JsonMigratableAttribute"/>.
    /// </summary>
    public static bool IsMigratable(Type type)
        => type.GetCustomAttribute<JsonMigratableAttribute>(inherit: true) is not null;

    /// <summary>
    /// Returns whether a converter other than the built-in one handles <paramref name="type"/>:
    /// a <see cref="JsonConverterAttribute"/> on the type or a matching entry in
    /// <see cref="JsonSerializerOptions.Converters"/>. Such a converter may read a different
    /// token family than the CLR type suggests, so shape classification cannot trust the type.
    /// </summary>
    public static bool HasConverterOverride(Type type, JsonSerializerOptions options)
    {
        if (type.GetCustomAttribute<JsonConverterAttribute>(inherit: false) is not null)
        {
            return true;
        }

        foreach (JsonConverter converter in options.Converters)
        {
            if (converter.CanConvert(type))
            {
                return true;
            }
        }

        return false;
    }
}
