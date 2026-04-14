using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Migrations;

internal sealed partial class JsonMigratableConverter<T>
{
    private MigratorReference? FindMigratorForNonObjectPayload(ref Utf8JsonReader reader, JsonTokenType tokenType)
    {
        if (tokenType is JsonTokenType.StartArray)
        {
            return FindEnumerableMigrator(ref reader);
        }

        // Primitive tokens: disambiguate by checking which source CLR type
        // is compatible with the JSON token type.
        MigratorReference? match = null;
        foreach (MigratorReference migrator in context.Migrators)
        {
            if (migrator.SourceTypeInfo.Kind != JsonTypeInfoKind.None)
            {
                continue;
            }

            if (!IsTokenCompatibleWithSourceType(tokenType, migrator.SourceType))
            {
                continue;
            }

            if (match is not null)
            {
                ThrowAmbiguousNonObjectMigrators(typeof(T));
            }

            match = migrator;
        }

        return match;
    }

    private MigratorReference? FindMigratorForDictionaryPayload(ref Utf8JsonReader reader)
    {
        return FindMigratorByValueToken(ref reader, JsonTypeInfoKind.Dictionary);
    }

    private MigratorReference? FindEnumerableMigrator(ref Utf8JsonReader reader)
    {
        return FindMigratorByValueToken(ref reader, JsonTypeInfoKind.Enumerable);
    }

    private MigratorReference? FindMigratorByValueToken(ref Utf8JsonReader reader, JsonTypeInfoKind kind)
    {
        MigratorReference? singleCandidate = FindSingleCandidateByKind(kind, out bool hasMultiple);

        if (singleCandidate is null)
        {
            return null;
        }

        if (!hasMultiple)
        {
            return singleCandidate;
        }

        // Multiple candidates — peek at the first value/element token to disambiguate.
        JsonTokenType? valueToken = PeekFirstValueToken(ref reader, kind);
        if (valueToken is null)
        {
            // Empty collection — can't disambiguate between multiple candidates.
            ThrowAmbiguousNonObjectMigrators(typeof(T));
        }

        MigratorReference? match = MatchByPrimitiveElementType(kind, valueToken.Value);

        if (match is null)
        {
            match = MatchByComplexElementType(ref reader, kind, valueToken.Value);
        }

        return match ?? singleCandidate;
    }

    private MigratorReference? FindSingleCandidateByKind(JsonTypeInfoKind kind, out bool hasMultiple)
    {
        MigratorReference? singleCandidate = null;
        hasMultiple = false;
        foreach (MigratorReference migrator in context.Migrators)
        {
            if (migrator.SourceTypeInfo.Kind != kind)
            {
                continue;
            }

            if (singleCandidate is null)
            {
                singleCandidate = migrator;
            }
            else
            {
                hasMultiple = true;
            }
        }

        return singleCandidate;
    }

    private MigratorReference? MatchByPrimitiveElementType(JsonTypeInfoKind kind, JsonTokenType valueToken)
    {
        MigratorReference? match = null;
        foreach (MigratorReference migrator in context.Migrators)
        {
            if (migrator.SourceTypeInfo.Kind != kind)
            {
                continue;
            }

            Type elementType = GetValueType(migrator.SourceType, kind);
            if (!IsTokenCompatibleWithSourceType(valueToken, elementType))
            {
                continue;
            }

            if (match is not null)
            {
                ThrowAmbiguousNonObjectMigrators(typeof(T));
            }

            match = migrator;
        }

        return match;
    }

    private MigratorReference? MatchByComplexElementType(ref Utf8JsonReader reader, JsonTypeInfoKind kind, JsonTokenType valueToken)
    {
        MigratorReference? match = null;

        if (valueToken is JsonTokenType.StartObject)
        {
            // Try to read the discriminator from the first element object
            // to match against migratable element types.
            match = FindMigratorByElementDiscriminator(ref reader, kind);
        }

        if (match is null && valueToken is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            match = MatchByElementShape(kind, valueToken);
        }

        return match;
    }

    private MigratorReference? MatchByElementShape(JsonTypeInfoKind kind, JsonTokenType valueToken)
    {
        // Fall back to candidates whose element type matches the JSON shape
        // (object → non-primitive non-enumerable type, array → enumerable element type).
        MigratorReference? match = null;
        foreach (MigratorReference migrator in context.Migrators)
        {
            if (migrator.SourceTypeInfo.Kind != kind)
            {
                continue;
            }

            Type elementType = GetValueType(migrator.SourceType, kind);
            if (IsKnownPrimitiveType(elementType))
            {
                continue;
            }

            bool isElementEnumerable = elementType.IsArray
                || (elementType.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(elementType));

            if (valueToken is JsonTokenType.StartArray && !isElementEnumerable)
            {
                continue;
            }

            if (valueToken is JsonTokenType.StartObject && isElementEnumerable)
            {
                continue;
            }

            if (match is not null)
            {
                ThrowAmbiguousNonObjectMigrators(typeof(T));
            }

            match = migrator;
        }

        return match;
    }

    private static JsonTokenType? PeekFirstValueToken(ref Utf8JsonReader reader, JsonTypeInfoKind kind)
    {
        var probe = reader;

        // For arrays, read past StartArray to get the first element.
        // For dictionaries, we're already past StartObject and the first PropertyName;
        // read past the property name to get the value.
        if (kind is JsonTypeInfoKind.Enumerable)
        {
            if (!probe.Read())
            {
                return null;
            }

            // Empty array
            if (probe.TokenType is JsonTokenType.EndArray)
            {
                return null;
            }

            return probe.TokenType;
        }

        // Dictionary: the reader probe is already at the first PropertyName position.
        // Skip past the property name to get the value token.
        if (!probe.Read())
        {
            return null;
        }

        // The property name — now read the value.
        if (probe.TokenType is JsonTokenType.PropertyName)
        {
            if (!probe.Read())
            {
                return null;
            }
        }

        return probe.TokenType;
    }

    private MigratorReference? FindMigratorByElementDiscriminator(ref Utf8JsonReader reader, JsonTypeInfoKind kind)
    {
        // Peek into the first element/value object to read its type discriminator.
        var probe = reader;

        // Navigate to the first element object's StartObject token.
        if (kind is JsonTypeInfoKind.Enumerable)
        {
            // Read past StartArray
            if (!probe.Read() || probe.TokenType is not JsonTokenType.StartObject)
            {
                return null;
            }
        }
        else
        {
            // Dictionary: probe is at the first PropertyName.
            // Skip the property name to get to the value.
            if (probe.TokenType is JsonTokenType.PropertyName)
            {
                if (!probe.Read() || probe.TokenType is not JsonTokenType.StartObject)
                {
                    return null;
                }
            }
            else if (probe.TokenType is not JsonTokenType.StartObject)
            {
                return null;
            }
        }

        // Now at StartObject. Read the first property.
        if (!probe.Read() || probe.TokenType is not JsonTokenType.PropertyName)
        {
            return null;
        }

        // Check each candidate's element type discriminator property name.
        MigratorReference? match = null;
        foreach (MigratorReference migrator in context.Migrators)
        {
            if (migrator.SourceTypeInfo.Kind != kind)
            {
                continue;
            }

            Type elementType = GetValueType(migrator.SourceType, kind);
            TypeMetadata? elementMetadata = TryGetMigratableMetadata(elementType);
            if (elementMetadata is null)
            {
                continue;
            }

            byte[] discriminatorPropertyNameUtf8 = System.Text.Encoding.UTF8.GetBytes(elementMetadata.DiscriminatorPropertyName);
            if (!probe.ValueTextEquals(discriminatorPropertyNameUtf8))
            {
                continue;
            }

            // Read the discriminator value.
            var valueProbe = probe;
            if (!valueProbe.Read() || valueProbe.TokenType is not JsonTokenType.String)
            {
                continue;
            }

            byte[] discriminatorUtf8 = System.Text.Encoding.UTF8.GetBytes(elementMetadata.Discriminator);
            if (valueProbe.ValueTextEquals(discriminatorUtf8))
            {
                if (match is not null)
                {
                    ThrowAmbiguousNonObjectMigrators(typeof(T));
                }

                match = migrator;
            }
        }

        return match;
    }

    private TypeMetadata? TryGetMigratableMetadata(Type type)
    {
        if (type.GetCustomAttribute<JsonMigratableAttribute>(inherit: true) is null)
        {
            return null;
        }

        return TypeMetadata.FromType(type);
    }

    private static Type GetValueType(Type collectionType, JsonTypeInfoKind kind)
    {
        if (collectionType.IsArray)
        {
            return collectionType.GetElementType()!;
        }

        // For generic collections (List<T>, Dictionary<K,V>, etc.),
        // the element type is the last generic argument.
        if (collectionType.IsGenericType)
        {
            Type[] args = collectionType.GetGenericArguments();
            return kind is JsonTypeInfoKind.Dictionary ? args[^1] : args[0];
        }

        return typeof(object);
    }

    private static bool IsKnownPrimitiveType(Type type)
    {
        return type == typeof(string)
            || type == typeof(bool)
            || Type.GetTypeCode(type) is
                TypeCode.Byte or TypeCode.SByte or
                TypeCode.Int16 or TypeCode.UInt16 or
                TypeCode.Int32 or TypeCode.UInt32 or
                TypeCode.Int64 or TypeCode.UInt64 or
                TypeCode.Single or TypeCode.Double or
                TypeCode.Decimal;
    }

    private static bool IsTokenCompatibleWithSourceType(JsonTokenType tokenType, Type sourceType)
    {
        return tokenType switch
        {
            JsonTokenType.String => sourceType == typeof(string),
            JsonTokenType.Number => Type.GetTypeCode(sourceType) is
                TypeCode.Byte or TypeCode.SByte or
                TypeCode.Int16 or TypeCode.UInt16 or
                TypeCode.Int32 or TypeCode.UInt32 or
                TypeCode.Int64 or TypeCode.UInt64 or
                TypeCode.Single or TypeCode.Double or
                TypeCode.Decimal,
            JsonTokenType.True or JsonTokenType.False => sourceType == typeof(bool),
            _ => false,
        };
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowAmbiguousNonObjectMigrators(Type targetType)
    {
        throw new JsonException(
            $"Multiple non-object migrators with ambiguous source types were found for target type '{targetType.FullName}'. " +
            $"Non-object payloads cannot be disambiguated when multiple migrators share the same JSON shape.");
    }
}
