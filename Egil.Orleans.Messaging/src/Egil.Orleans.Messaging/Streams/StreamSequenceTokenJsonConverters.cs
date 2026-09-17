using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orleans.Providers.Streams.Common;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Streams;

/// <summary>
/// Process-wide registry of explicitly registered JSON converters for concrete
/// <see cref="StreamSequenceToken"/> types.
/// </summary>
/// <remarks>
/// <see cref="StreamCursor"/> and <see cref="Tracking.MessageTracker"/> use
/// this registry to write a small discriminator envelope and then delegate the
/// token payload directly to the registered <see cref="JsonConverter{T}"/>.
/// Converters should be registered during silo startup before persisted stream
/// positions are serialized or deserialized.
/// The envelope reader accepts properties in any order and ignores unknown
/// properties. Registered converters are responsible for providing the same
/// forward-compatible behavior inside their payloads.
/// </remarks>
public static class StreamSequenceTokenJsonConverters
{
    private static readonly object Sync = new();

    private static ImmutableArray<Registration> registrations =
    [
        Registration.Create("event-sequence", new EventSequenceTokenJsonConverter()),
        Registration.Create("event-sequence-v2", new EventSequenceTokenV2JsonConverter())
    ];

    /// <summary>
    /// Registers a converter for a concrete stream sequence token type.
    /// </summary>
    /// <typeparam name="TToken">The concrete token type handled by the converter.</typeparam>
    /// <param name="typeDescriptor">Stable discriminator written to the token JSON envelope.</param>
    /// <param name="converter">The converter that reads and writes the token payload.</param>
    /// <remarks>
    /// Re-registering the same descriptor with the same token type and converter type
    /// is a no-op. Several registrars legitimately want the same converter in place —
    /// a silo provider setup, a test fixture on in-memory storage, an offline grain
    /// state reader — and throwing on that would make the order they run in
    /// significant. Only a genuine conflict, where a different converter claims a
    /// descriptor someone else already owns, throws.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a different converter already owns the same type descriptor.
    /// </exception>
    public static void Register<TToken>(
        string typeDescriptor,
        JsonConverter<TToken> converter)
        where TToken : StreamSequenceToken
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeDescriptor);
        ArgumentNullException.ThrowIfNull(converter);

        var registration = Registration.Create(typeDescriptor, converter);
        if (!TryRegister(registration, out var conflicting))
        {
            throw new InvalidOperationException(
                $"A stream sequence token JSON converter for '{registration.TypeDescriptor}' is already registered by " +
                $"'{conflicting.ConverterType.FullName}'.");
        }
    }

    /// <summary>
    /// Registers a converter with a public parameterless constructor for a
    /// concrete stream sequence token type.
    /// </summary>
    /// <typeparam name="TToken">The concrete token type handled by the converter.</typeparam>
    /// <typeparam name="TConverter">The converter type.</typeparam>
    /// <param name="typeDescriptor">Stable discriminator written to the token JSON envelope.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a different converter already owns the same type descriptor.
    /// </exception>
    public static void Register<TToken, TConverter>(string typeDescriptor)
        where TToken : StreamSequenceToken
        where TConverter : JsonConverter<TToken>, new() =>
        Register(typeDescriptor, new TConverter());

    /// <summary>
    /// Registers a converter for a concrete stream sequence token type unless a
    /// different converter already owns the type descriptor.
    /// </summary>
    /// <typeparam name="TToken">The concrete token type handled by the converter.</typeparam>
    /// <param name="typeDescriptor">Stable discriminator written to the token JSON envelope.</param>
    /// <param name="converter">The converter that reads and writes the token payload.</param>
    /// <returns>
    /// <see langword="true"/> when the registry holds an equivalent converter for
    /// <paramref name="typeDescriptor"/> once this call returns, whether it was added
    /// now or an identical registration was already present; <see langword="false"/>
    /// when a different converter owns the descriptor and was left in place.
    /// </returns>
    /// <remarks>
    /// The return value answers "does the registry now hold my converter for this
    /// descriptor?", not "did this call mutate the registry". That lets a caller treat
    /// <see langword="false"/> as a genuine conflict worth reporting, while an
    /// identical re-registration stays a silent success, exactly as with
    /// <see cref="Register{TToken}(string, JsonConverter{TToken})"/>.
    /// </remarks>
    public static bool TryRegister<TToken>(
        string typeDescriptor,
        JsonConverter<TToken> converter)
        where TToken : StreamSequenceToken
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeDescriptor);
        ArgumentNullException.ThrowIfNull(converter);

        return TryRegister(Registration.Create(typeDescriptor, converter), out _);
    }

    /// <summary>
    /// Registers a converter with a public parameterless constructor for a concrete
    /// stream sequence token type unless a different converter already owns the type
    /// descriptor.
    /// </summary>
    /// <typeparam name="TToken">The concrete token type handled by the converter.</typeparam>
    /// <typeparam name="TConverter">The converter type.</typeparam>
    /// <param name="typeDescriptor">Stable discriminator written to the token JSON envelope.</param>
    /// <returns>
    /// <see langword="true"/> when the registry holds an equivalent converter for
    /// <paramref name="typeDescriptor"/> once this call returns; <see langword="false"/>
    /// when a different converter owns the descriptor and was left in place.
    /// </returns>
    public static bool TryRegister<TToken, TConverter>(string typeDescriptor)
        where TToken : StreamSequenceToken
        where TConverter : JsonConverter<TToken>, new() =>
        TryRegister<TToken>(typeDescriptor, new TConverter());

    internal static void Write(Utf8JsonWriter writer, StreamSequenceToken token, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(token);

        var registration = registrations.FirstOrDefault(x => x.CanWrite(token))
            ?? throw CreateUnsupportedTokenException(token.GetType().FullName ?? token.GetType().Name);

        writer.WriteStartObject();
        writer.WriteString("Kind", registration.TypeDescriptor);
        writer.WritePropertyName("Payload");
        registration.Write(writer, token, options);
        writer.WriteEndObject();
    }

    internal static StreamSequenceToken Read(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.None && !reader.Read())
        {
            throw new JsonException("Unexpected end of stream sequence token JSON.");
        }

        if (reader.TokenType is not JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected stream sequence token object, got '{reader.TokenType}'.");
        }

        Registration? registration = null;
        var hasKind = false;
        var hasPayload = false;
        JsonElement bufferedPayload = default;
        StreamSequenceToken? token = null;
        var tokenRead = false;

        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.EndObject)
            {
                if (!hasKind)
                {
                    throw new JsonException("Missing stream sequence token property 'Kind'.");
                }

                if (!hasPayload)
                {
                    throw new JsonException("Missing stream sequence token property 'Payload'.");
                }

                return tokenRead
                    ? token!
                    : ReadBufferedPayload(bufferedPayload, registration!, options);
            }

            if (reader.TokenType is not JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected stream sequence token property, got '{reader.TokenType}'.");
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
            {
                throw new JsonException("Unexpected end of stream sequence token JSON.");
            }

            switch (propertyName)
            {
                case "Kind":
                    ThrowIfDuplicate(hasKind, "Kind");
                    if (reader.TokenType is not JsonTokenType.String)
                    {
                        throw new JsonException("Stream sequence token property 'Kind' must be a string.");
                    }

                    var typeDescriptor = reader.GetString();
                    if (string.IsNullOrWhiteSpace(typeDescriptor))
                    {
                        throw new JsonException("Stream sequence token property 'Kind' must not be empty.");
                    }

                    registration = registrations.FirstOrDefault(x => x.TypeDescriptor == typeDescriptor);
                    if (registration is null)
                    {
                        throw new JsonException(CreateUnsupportedTokenMessage(typeDescriptor));
                    }

                    hasKind = true;
                    break;
                case "Payload":
                {
                    ThrowIfDuplicate(hasPayload, "Payload");
                    if (registration is null)
                    {
                        // JSON properties are unordered, so payload can arrive before
                        // its discriminator. Buffer only this value until Kind selects
                        // the registered converter; canonical Kind-first input streams.
                        using var document = JsonDocument.ParseValue(ref reader);
                        bufferedPayload = document.RootElement.Clone();
                    }
                    else
                    {
                        token = registration.Read(ref reader, options);
                        tokenRead = true;
                    }

                    hasPayload = true;
                    break;
                }
                default:
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException("Unexpected end of stream sequence token JSON.");
    }

    internal static NotSupportedException CreateUnsupportedTokenException(string tokenIdentifier) =>
        new(CreateUnsupportedTokenMessage(tokenIdentifier));

    private static bool TryRegister(
        Registration registration,
        [NotNullWhen(false)] out Registration? conflicting)
    {
        lock (Sync)
        {
            var existing = registrations.FirstOrDefault(x => x.TypeDescriptor == registration.TypeDescriptor);
            if (existing is null)
            {
                registrations = registrations.Add(registration);
                conflicting = null;
                return true;
            }

            // An identical re-registration keeps the registration already in the array
            // rather than replacing it. Readers see the same behaviour either way, and
            // not touching the array keeps concurrent lock-free readers on a snapshot
            // they are already iterating.
            if (existing.TokenType == registration.TokenType
                && existing.ConverterType == registration.ConverterType)
            {
                conflicting = null;
                return true;
            }

            conflicting = existing;
            return false;
        }
    }

    private static StreamSequenceToken ReadBufferedPayload(
        JsonElement payload,
        Registration registration,
        JsonSerializerOptions options)
    {
        var bytes = Encoding.UTF8.GetBytes(payload.GetRawText());
        var payloadReader = new Utf8JsonReader(
            bytes,
            new JsonReaderOptions
            {
                AllowTrailingCommas = options.AllowTrailingCommas,
                CommentHandling = options.ReadCommentHandling,
                MaxDepth = options.MaxDepth
            });
        if (!payloadReader.Read())
        {
            throw new JsonException("Unexpected end of stream sequence token payload.");
        }

        var token = registration.Read(ref payloadReader, options);
        if (payloadReader.Read())
        {
            throw new JsonException("Expected a single stream sequence token payload.");
        }

        return token;
    }

    private static void ThrowIfDuplicate(bool hasProperty, string propertyName)
    {
        if (hasProperty)
        {
            throw new JsonException($"Duplicate stream sequence token property '{propertyName}'.");
        }
    }

    private static string CreateUnsupportedTokenMessage(string tokenIdentifier) =>
        $"Unsupported stream sequence token '{tokenIdentifier}'. " +
        "Register a JsonConverter for provider-specific StreamSequenceToken types before " +
        "serializing or deserializing MessageTracker or StreamCursor values.";

    private abstract class Registration
    {
        protected Registration(string typeDescriptor, Type tokenType, Type converterType)
        {
            TypeDescriptor = typeDescriptor;
            TokenType = tokenType;
            ConverterType = converterType;
        }

        public string TypeDescriptor { get; }

        public Type TokenType { get; }

        public Type ConverterType { get; }

        public static Registration Create<TToken>(
            string typeDescriptor,
            JsonConverter<TToken> converter)
            where TToken : StreamSequenceToken =>
            new Registration<TToken>(typeDescriptor, converter);

        public bool CanWrite(StreamSequenceToken token) =>
            token.GetType() == TokenType;

        public abstract void Write(Utf8JsonWriter writer, StreamSequenceToken token, JsonSerializerOptions options);

        public abstract StreamSequenceToken Read(ref Utf8JsonReader reader, JsonSerializerOptions options);
    }

    private sealed class Registration<TToken> : Registration
        where TToken : StreamSequenceToken
    {
        private readonly JsonConverter<TToken> converter;

        public Registration(string typeDescriptor, JsonConverter<TToken> converter)
            : base(typeDescriptor, typeof(TToken), converter.GetType())
        {
            this.converter = converter;
        }

        public override void Write(Utf8JsonWriter writer, StreamSequenceToken token, JsonSerializerOptions options) =>
            converter.Write(writer, (TToken)token, options);

        public override StreamSequenceToken Read(ref Utf8JsonReader reader, JsonSerializerOptions options) =>
            converter.Read(ref reader, typeof(TToken), options)
            ?? throw new JsonException($"Converter '{converter.GetType().FullName}' returned null.");
    }

    private sealed class EventSequenceTokenJsonConverter : JsonConverter<EventSequenceToken>
    {
        public override EventSequenceToken Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is not JsonTokenType.StartObject)
            {
                throw new JsonException($"Expected {nameof(EventSequenceToken)} object, got '{reader.TokenType}'.");
            }

            var (sequenceNumber, eventIndex) = ReadEventSequenceTokenProperties(
                ref reader,
                nameof(EventSequenceToken));

            return new EventSequenceToken(sequenceNumber, eventIndex);
        }

        public override void Write(Utf8JsonWriter writer, EventSequenceToken value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("SequenceNumber", value.SequenceNumber);
            writer.WriteNumber("EventIndex", value.EventIndex);
            writer.WriteEndObject();
        }
    }

    private sealed class EventSequenceTokenV2JsonConverter : JsonConverter<EventSequenceTokenV2>
    {
        public override EventSequenceTokenV2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is not JsonTokenType.StartObject)
            {
                throw new JsonException($"Expected {nameof(EventSequenceTokenV2)} object, got '{reader.TokenType}'.");
            }

            var (sequenceNumber, eventIndex) = ReadEventSequenceTokenProperties(
                ref reader,
                nameof(EventSequenceTokenV2));

            return new EventSequenceTokenV2(sequenceNumber, eventIndex);
        }

        public override void Write(Utf8JsonWriter writer, EventSequenceTokenV2 value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("SequenceNumber", value.SequenceNumber);
            writer.WriteNumber("EventIndex", value.EventIndex);
            writer.WriteEndObject();
        }
    }

    private static (long SequenceNumber, int EventIndex) ReadEventSequenceTokenProperties(
        ref Utf8JsonReader reader,
        string typeName)
    {
        long sequenceNumber = 0;
        var hasSequenceNumber = false;
        var eventIndex = 0;
        var hasEventIndex = false;

        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.EndObject)
            {
                return (
                    hasSequenceNumber
                        ? sequenceNumber
                        : throw new JsonException($"Missing {typeName} property 'SequenceNumber'."),
                    hasEventIndex
                        ? eventIndex
                        : throw new JsonException($"Missing {typeName} property 'EventIndex'."));
            }

            if (reader.TokenType is not JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected {typeName} property, got '{reader.TokenType}'.");
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
            {
                throw new JsonException($"Unexpected end of {typeName} JSON.");
            }

            switch (propertyName)
            {
                case "SequenceNumber":
                    ThrowIfDuplicate(hasSequenceNumber, "SequenceNumber");
                    if (reader.TokenType is not JsonTokenType.Number
                        || !reader.TryGetInt64(out sequenceNumber))
                    {
                        throw new JsonException("Property 'SequenceNumber' must be a 64-bit integer.");
                    }

                    hasSequenceNumber = true;
                    break;
                case "EventIndex":
                    ThrowIfDuplicate(hasEventIndex, "EventIndex");
                    if (reader.TokenType is not JsonTokenType.Number
                        || !reader.TryGetInt32(out eventIndex))
                    {
                        throw new JsonException("Property 'EventIndex' must be a 32-bit integer.");
                    }

                    hasEventIndex = true;
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException($"Unexpected end of {typeName} JSON.");
    }
}
