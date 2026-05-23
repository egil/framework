using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.Messaging;

/// <summary>
/// STJ converter for <see cref="OutboxSequenceToken"/>. Serializes and
/// deserializes the token's <see cref="OutboxSequenceToken.Sender"/>,
/// <see cref="OutboxSequenceToken.SequenceNumber"/>, and
/// <see cref="OutboxSequenceToken.Epoch"/> properties.
/// </summary>
/// <remarks>
/// <para>
/// Registered on <see cref="OutboxSequenceToken"/> via <c>[JsonConverter]</c>.
/// STJ discovers the attribute automatically — no user-side
/// <see cref="JsonSerializerOptions"/> configuration needed.
/// </para>
/// </remarks>
internal sealed class OutboxSequenceTokenJsonConverter : JsonConverter<OutboxSequenceToken>
{
    /// <inheritdoc/>
    public override OutboxSequenceToken? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, OutboxSequenceToken value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}
