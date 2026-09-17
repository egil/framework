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
    /// a <see cref="JsonConverterAttribute"/> on the type, a matching entry in
    /// <see cref="JsonSerializerOptions.Converters"/>, or a converter supplied by the type info
    /// resolver. Such a converter may read a different token family than the CLR type suggests,
    /// so shape classification cannot trust the type.
    /// </summary>
    public static bool HasConverterOverride(Type type, JsonSerializerOptions options)
    {
        // A Nullable<T> source is read by T's converter wrapped in the nullable converter, so an
        // override registered for T counts for T? as well.
        if (Nullable.GetUnderlyingType(type) is { } underlyingType && HasConverterOverride(underlyingType, options))
        {
            return true;
        }

        // The resolved contract is the truth: a custom resolver may bypass options.Converters
        // entirely, so the list cannot be trusted on its own.
        Type resolvedConverter = options.GetTypeInfo(type).Converter.GetType();
        if (resolvedConverter.IsGenericType && resolvedConverter.GetGenericTypeDefinition() == typeof(JsonMigratableConverter<>))
        {
            return false;
        }

        if (resolvedConverter.Assembly != typeof(JsonSerializer).Assembly)
        {
            return true;
        }

        // A built-in converter type may still be a configured override (JsonStringEnumConverter
        // yields the built-in enum converter). STJ precedence: the first matching entry in
        // options.Converters wins over [JsonConverter] on the type. A resolver substituting a
        // differently configured built-in instance without either registration is not detected
        // (documented limitation).
        foreach (JsonConverter converter in options.Converters)
        {
            if (converter.CanConvert(type))
            {
                return converter is not JsonMigratableConverterFactory;
            }
        }

        return type.GetCustomAttribute<JsonConverterAttribute>(inherit: false) is not null;
    }
}
