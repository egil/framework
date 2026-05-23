using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.Messaging;

/// <summary>
/// STJ converter for <see cref="StreamCursor"/>. Serializes and
/// deserializes the cursor's <see cref="StreamCursor.StreamId"/> and
/// sequence token.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StreamCursor"/> wraps a <c>StreamId</c> and an optional
/// <c>StreamSequenceToken</c>. The token is polymorphic — it may be an
/// <see cref="EnrichedEventHubSequenceToken"/>,
/// <c>EventHubSequenceTokenV2</c>,
/// or another provider-specific subclass. The converter round-trips the
/// concrete type via a discriminator so deserialization restores the
/// original token type.
/// </para>
/// <para>
/// Registered on <see cref="StreamCursor"/> via <c>[JsonConverter]</c>.
/// STJ discovers the attribute automatically — no user-side
/// <see cref="JsonSerializerOptions"/> configuration needed.
/// </para>
/// </remarks>
internal sealed class StreamCursorJsonConverter : JsonConverter<StreamCursor>
{
    /// <inheritdoc/>
    public override StreamCursor? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, StreamCursor value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}
