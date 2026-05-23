using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.Messaging;

/// <summary>
/// STJ converter for <see cref="MessageTracker"/>. Serializes and
/// deserializes the tracker's internal stream-position and outbox-position
/// dictionaries without exposing private fields.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MessageTracker"/> is a sealed class with private
/// <see cref="System.Collections.Immutable.ImmutableDictionary{TKey, TValue}"/>
/// backing fields. Exposing them via <c>[JsonInclude]</c> would leak
/// internal structure and weaken encapsulation. This converter controls
/// the exact wire format.
/// </para>
/// <para>
/// Registered on <see cref="MessageTracker"/> via <c>[JsonConverter]</c>.
/// STJ discovers the attribute automatically — no user-side
/// <see cref="JsonSerializerOptions"/> configuration needed.
/// </para>
/// </remarks>
internal sealed class MessageTrackerJsonConverter : JsonConverter<MessageTracker>
{
    /// <inheritdoc/>
    public override MessageTracker? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, MessageTracker value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}
