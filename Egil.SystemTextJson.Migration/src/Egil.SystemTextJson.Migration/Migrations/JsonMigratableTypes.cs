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
    /// Returns the type that carries <see cref="JsonMigratableAttribute"/> when <paramref name="type"/>
    /// is migratable or a <see cref="Nullable{T}"/> of a migratable struct, otherwise <see langword="null"/>.
    /// STJ reads <c>T?</c> through <c>T</c>'s converter, so the migration contract (discriminator,
    /// migrators) of a nullable element or union case is that of the underlying type.
    /// </summary>
    public static Type? GetMigratableType(Type type)
    {
        Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return IsMigratable(underlyingType) ? underlyingType : null;
    }

    /// <summary>
    /// Returns whether <paramref name="type"/> is a C# union (marked with
    /// <c>System.Runtime.CompilerServices.UnionAttribute</c>) that has, directly or through a
    /// nested union, a case annotated with <see cref="JsonMigratableAttribute"/>. The cases are
    /// read from the compiler-generated single-parameter constructors, the same source
    /// System.Text.Json uses, because no options are available where this is needed.
    /// </summary>
    public static bool IsUnionWithMigratableCase(Type type)
    {
        if (!IsUnion(type))
        {
            return false;
        }

        foreach (ConstructorInfo constructor in type.GetConstructors())
        {
            if (constructor.GetParameters() is not [{ ParameterType: { } caseType }])
            {
                continue;
            }

            if (GetMigratableType(caseType) is not null || IsUnionWithMigratableCase(caseType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnion(Type type)
    {
        foreach (CustomAttributeData attribute in type.GetCustomAttributesData())
        {
            if (attribute.AttributeType.FullName == "System.Runtime.CompilerServices.UnionAttribute")
            {
                return true;
            }
        }

        return false;
    }

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
                return true;
            }
        }

        return type.GetCustomAttribute<JsonConverterAttribute>(inherit: false) is not null;
    }
}
