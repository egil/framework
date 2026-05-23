using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.Messaging;

/// <summary>
/// STJ converter factory that creates closed <see cref="JsonConverter{T}"/>
/// instances for <see cref="OutboxMessageEnvelope{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered on <see cref="OutboxMessageEnvelope{T}"/> via
/// <c>[JsonConverter]</c>. STJ discovers the attribute automatically —
/// no user-side <see cref="JsonSerializerOptions"/> configuration needed.
/// </para>
/// </remarks>
internal sealed class OutboxMessageEnvelopeJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}
