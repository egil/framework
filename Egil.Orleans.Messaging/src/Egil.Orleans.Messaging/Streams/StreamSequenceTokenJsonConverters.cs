using System.Collections.Immutable;
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
    /// <exception cref="InvalidOperationException">
    /// Thrown when another converter already owns the same type descriptor.
    /// </exception>
    public static void Register<TToken>(
        string typeDescriptor,
        JsonConverter<TToken> converter)
        where TToken : StreamSequenceToken
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeDescriptor);
        ArgumentNullException.ThrowIfNull(converter);

        Register(Registration.Create(typeDescriptor, converter));
    }

    /// <summary>
    /// Registers a converter with a public parameterless constructor for a
    /// concrete stream sequence token type.
    /// </summary>
    /// <typeparam name="TToken">The concrete token type handled by the converter.</typeparam>
    /// <typeparam name="TConverter">The converter type.</typeparam>
    /// <param name="typeDescriptor">Stable discriminator written to the token JSON envelope.</param>
    public static void Register<TToken, TConverter>(string typeDescriptor)
        where TToken : StreamSequenceToken
        where TConverter : JsonConverter<TToken>, new() =>
        Register(typeDescriptor, new TConverter());

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

        var typeDescriptor = ReadRequiredStringProperty(ref reader, "Kind");
        var registration = registrations.FirstOrDefault(x => x.TypeDescriptor == typeDescriptor);
        if (registration is null)
        {
            throw new JsonException(CreateUnsupportedTokenMessage(typeDescriptor));
        }

        ReadRequiredPropertyName(ref reader, "Payload");
        if (!reader.Read())
        {
            throw new JsonException("Unexpected end of stream sequence token payload.");
        }

        var token = registration.Read(ref reader, options);

        if (!reader.Read() || reader.TokenType is not JsonTokenType.EndObject)
        {
            throw new JsonException("Expected end of stream sequence token object.");
        }

        return token;
    }

    internal static NotSupportedException CreateUnsupportedTokenException(string tokenIdentifier) =>
        new(CreateUnsupportedTokenMessage(tokenIdentifier));

    private static void Register(Registration registration)
    {
        lock (Sync)
        {
            var existing = registrations.FirstOrDefault(x => x.TypeDescriptor == registration.TypeDescriptor);
            if (existing is null)
            {
                registrations = registrations.Add(registration);
                return;
            }

            if (existing.TokenType == registration.TokenType
                && existing.ConverterType == registration.ConverterType)
            {
                return;
            }

            throw new InvalidOperationException(
                $"A stream sequence token JSON converter for '{registration.TypeDescriptor}' is already registered by " +
                $"'{existing.ConverterType.FullName}'.");
        }
    }

    private static string ReadRequiredStringProperty(ref Utf8JsonReader reader, string propertyName)
    {
        ReadRequiredPropertyName(ref reader, propertyName);
        if (!reader.Read() || reader.TokenType is not JsonTokenType.String)
        {
            throw new JsonException($"Stream sequence token property '{propertyName}' must be a string.");
        }

        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException($"Stream sequence token property '{propertyName}' must not be empty.");
        }

        return value;
    }

    private static void ReadRequiredPropertyName(ref Utf8JsonReader reader, string propertyName)
    {
        if (!reader.Read() || reader.TokenType is not JsonTokenType.PropertyName)
        {
            throw new JsonException($"Expected stream sequence token property '{propertyName}'.");
        }

        if (!reader.ValueTextEquals(propertyName))
        {
            throw new JsonException($"Expected stream sequence token property '{propertyName}', got '{reader.GetString()}'.");
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

            var sequenceNumber = ReadRequiredInt64Property(ref reader, "SequenceNumber");
            var eventIndex = ReadRequiredInt32Property(ref reader, "EventIndex");
            ReadEndObject(ref reader);

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

            var sequenceNumber = ReadRequiredInt64Property(ref reader, "SequenceNumber");
            var eventIndex = ReadRequiredInt32Property(ref reader, "EventIndex");
            ReadEndObject(ref reader);

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

    private static long ReadRequiredInt64Property(ref Utf8JsonReader reader, string propertyName)
    {
        ReadRequiredPropertyName(ref reader, propertyName);
        if (!reader.Read() || reader.TokenType is not JsonTokenType.Number)
        {
            throw new JsonException($"Property '{propertyName}' must be a number.");
        }

        return reader.GetInt64();
    }

    private static int ReadRequiredInt32Property(ref Utf8JsonReader reader, string propertyName)
    {
        ReadRequiredPropertyName(ref reader, propertyName);
        if (!reader.Read() || reader.TokenType is not JsonTokenType.Number)
        {
            throw new JsonException($"Property '{propertyName}' must be a number.");
        }

        return reader.GetInt32();
    }

    private static void ReadEndObject(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType is not JsonTokenType.EndObject)
        {
            throw new JsonException("Expected end of JSON object.");
        }
    }
}
