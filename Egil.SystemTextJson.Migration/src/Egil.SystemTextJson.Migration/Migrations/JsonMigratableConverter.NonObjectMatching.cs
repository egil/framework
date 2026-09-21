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

        // Primitive tokens are matched in tiers so that every source 1.x selected is still selected
        // before any source 2.0 added can compete with it: first the sources 1.x's TypeCode rule
        // matched, plain ones ahead of ones behind a converter override (1.x reported that pair as
        // ambiguous; the plain source is the safer pick), then the shapes 2.0 recognises, then a
        // quoted number for numeric sources under AllowReadingFromString ("NaN"/"Infinity" for
        // floating-point sources only), and last the 2.0 shapes of overridden sources. Within a
        // tier two candidates are ambiguous.
        MigratorReference? match = MatchPrimitive(tokenType, MatchTier.PlainLegacy)
            ?? MatchPrimitive(tokenType, MatchTier.OverriddenLegacy)
            ?? MatchPrimitive(tokenType, MatchTier.PlainWidened);

        if (match is null && tokenType is JsonTokenType.String)
        {
            match = MatchPrimitive(tokenType, MatchTier.QuotedNumber, SourceValueShapes.IsNamedFloatingPointLiteral(ref reader));
        }

        return match ?? MatchPrimitive(tokenType, MatchTier.OverriddenWidened);
    }

    private enum MatchTier
    {
        PlainLegacy,
        OverriddenLegacy,
        PlainWidened,
        QuotedNumber,
        OverriddenWidened,
    }

    private MigratorReference? MatchPrimitive(JsonTokenType tokenType, MatchTier tier, bool namedLiteral = false)
    {
        MigratorReference? match = null;
        foreach (MigratorReference migrator in context.Migrators)
        {
            // SourceShape is classified for plain sources only (byte[] reports Kind Enumerable but
            // is read from a base64 string, and custom scalar converters report Kind None), and
            // OverriddenShape carries the CLR shape of a source behind a converter override.
            SourceValueShape shape;
            bool allowQuoted = false;
            switch (tier)
            {
                case MatchTier.PlainLegacy when migrator.IsLegacyShape:
                case MatchTier.PlainWidened when !migrator.IsLegacyShape:
                    shape = migrator.SourceShape;
                    break;
                case MatchTier.OverriddenLegacy when migrator.IsLegacyShape:
                case MatchTier.OverriddenWidened when !migrator.IsLegacyShape:
                    shape = migrator.OverriddenShape;
                    break;
                case MatchTier.QuotedNumber:
                    shape = migrator.SourceShape;
                    allowQuoted = namedLiteral ? migrator.AllowsNamedFloatingPointLiterals : migrator.AllowsQuotedNumbers;
                    break;
                default:
                    continue;
            }

            if (shape is SourceValueShape.Unknown || !SourceValueShapes.IsTokenCompatible(tokenType, shape, allowQuoted))
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
        if (!TryPeekFirstValueToken(ref reader, kind, out JsonTokenType valueToken, out bool namedLiteral))
        {
            // Empty collection — can't disambiguate between multiple candidates.
            ThrowAmbiguousNonObjectMigrators(typeof(T));
        }

        // An element discriminator identifies its collection exactly, so it is checked before
        // any shape-based rule, including the guard below.
        MigratorReference? match = valueToken is JsonTokenType.StartObject
            ? FindMigratorByElementDiscriminator(ref reader, kind)
            : null;

        if (match is not null)
        {
            return match;
        }

        // A candidate whose element converter is overridden may accept any element, so no
        // other candidate can be chosen safely by element shape.
        if (HasOverriddenElementCandidate(kind))
        {
            ThrowAmbiguousNonObjectMigrators(typeof(T));
        }

        // Same precedence as top-level primitives: element types 1.x matched first, then the
        // element shapes 2.0 added, then quoted numbers for numeric element types when number
        // handling allows reading from strings.
        match = MatchByPrimitiveElementType(kind, valueToken, ElementTier.Legacy)
            ?? MatchByPrimitiveElementType(kind, valueToken, ElementTier.Widened);

        if (match is null && valueToken is JsonTokenType.String)
        {
            match = MatchByPrimitiveElementType(kind, valueToken, ElementTier.QuotedNumber, namedLiteral);
        }

        if (match is null)
        {
            match = MatchByComplexElementType(kind, valueToken);
        }

        // A null first value says nothing about the element type, so it cannot pick one of
        // several candidates; registration order must not decide.
        if (match is null && valueToken is JsonTokenType.Null)
        {
            ThrowAmbiguousNonObjectMigrators(typeof(T));
        }

        return match ?? singleCandidate;
    }

    private bool HasOverriddenElementCandidate(JsonTypeInfoKind kind)
    {
        foreach (MigratorReference migrator in context.Migrators)
        {
            if (IsCollectionCandidate(migrator, kind) && migrator.ElementConverterOverridden)
            {
                return true;
            }
        }

        return false;
    }

    // byte[] reports Kind Enumerable but is read from a base64 string, so scalar-shaped sources
    // never compete for array or dictionary payloads.
    private static bool IsCollectionCandidate(MigratorReference migrator, JsonTypeInfoKind kind)
        => migrator.SourceTypeInfo.Kind == kind && migrator.SourceShape is SourceValueShape.Unknown;

    private MigratorReference? FindSingleCandidateByKind(JsonTypeInfoKind kind, out bool hasMultiple)
    {
        MigratorReference? singleCandidate = null;
        hasMultiple = false;
        foreach (MigratorReference migrator in context.Migrators)
        {
            if (!IsCollectionCandidate(migrator, kind))
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

    private enum ElementTier
    {
        Legacy,
        Widened,
        QuotedNumber,
    }

    private MigratorReference? MatchByPrimitiveElementType(JsonTypeInfoKind kind, JsonTokenType valueToken, ElementTier tier, bool namedLiteral = false)
    {
        MigratorReference? match = null;
        foreach (MigratorReference migrator in context.Migrators)
        {
            if (!IsCollectionCandidate(migrator, kind))
            {
                continue;
            }

            bool allowQuoted = false;
            switch (tier)
            {
                case ElementTier.Legacy when migrator.ElementIsLegacyShape:
                case ElementTier.Widened when !migrator.ElementIsLegacyShape:
                    break;
                case ElementTier.QuotedNumber:
                    allowQuoted = namedLiteral ? migrator.ElementAllowsNamedFloatingPointLiterals : migrator.ElementAllowsQuotedNumbers;
                    break;
                default:
                    continue;
            }

            if (!SourceValueShapes.IsTokenCompatible(valueToken, migrator.ElementShape, allowQuoted))
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

    private MigratorReference? MatchByComplexElementType(JsonTypeInfoKind kind, JsonTokenType valueToken)
        => valueToken is JsonTokenType.StartObject or JsonTokenType.StartArray
            ? MatchByElementShape(kind, valueToken)
            : null;

    private MigratorReference? MatchByElementShape(JsonTypeInfoKind kind, JsonTokenType valueToken)
    {
        // Fall back to candidates whose element type matches the JSON shape
        // (object → non-primitive non-enumerable type, array → enumerable element type).
        MigratorReference? match = null;
        foreach (MigratorReference migrator in context.Migrators)
        {
            if (!IsCollectionCandidate(migrator, kind))
            {
                continue;
            }

            if (migrator.ElementShape is not SourceValueShape.Unknown)
            {
                continue;
            }

            if (valueToken is JsonTokenType.StartArray && migrator.ElementKind is not JsonTypeInfoKind.Enumerable)
            {
                continue;
            }

            if (valueToken is JsonTokenType.StartObject && migrator.ElementKind is not (JsonTypeInfoKind.Object or JsonTypeInfoKind.Dictionary))
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

    private static bool TryPeekFirstValueToken(ref Utf8JsonReader reader, JsonTypeInfoKind kind, out JsonTokenType valueToken, out bool namedLiteral)
    {
        var probe = reader;
        valueToken = JsonTokenType.None;
        namedLiteral = false;

        // For arrays, read past StartArray to get the first element.
        // For dictionaries, we're already past StartObject and the first PropertyName;
        // read past the property name to get the value.
        if (kind is JsonTypeInfoKind.Enumerable)
        {
            // Empty array or truncated input.
            if (!probe.Read() || probe.TokenType is JsonTokenType.EndArray)
            {
                return false;
            }
        }
        else
        {
            // Dictionary: the reader probe is already at the first PropertyName position.
            // Skip past the property name to get the value token.
            if (!probe.Read())
            {
                return false;
            }

            if (probe.TokenType is JsonTokenType.PropertyName && !probe.Read())
            {
                return false;
            }
        }

        valueToken = probe.TokenType;
        namedLiteral = valueToken is JsonTokenType.String && SourceValueShapes.IsNamedFloatingPointLiteral(ref probe);
        return true;
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
            if (!IsCollectionCandidate(migrator, kind))
            {
                continue;
            }

            if (migrator.ElementDiscriminatorPropertyNameUtf8 is null)
            {
                continue;
            }

            if (!probe.ValueTextEquals(migrator.ElementDiscriminatorPropertyNameUtf8))
            {
                continue;
            }

            // Read the discriminator value.
            var valueProbe = probe;
            if (!valueProbe.Read() || valueProbe.TokenType is not JsonTokenType.String)
            {
                continue;
            }

            if (valueProbe.ValueTextEquals(migrator.ElementDiscriminatorUtf8!))
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

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowAmbiguousNonObjectMigrators(Type targetType)
    {
        throw new JsonException(
            $"Multiple non-object migrators with ambiguous source types were found for target type '{targetType.FullName}'. " +
            $"Non-object payloads cannot be disambiguated when multiple migrators share the same JSON shape.");
    }
}
