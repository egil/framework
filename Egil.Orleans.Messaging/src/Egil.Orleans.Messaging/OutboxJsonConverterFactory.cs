using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.Messaging;

/// <summary>
/// STJ converter factory that creates closed <see cref="JsonConverter{T}"/>
/// instances for <see cref="Outbox{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered on <see cref="Outbox{T}"/> via <c>[JsonConverter]</c>. STJ
/// discovers the attribute automatically — no user-side
/// <see cref="JsonSerializerOptions"/> configuration needed.
/// </para>
/// <para>
/// The converter serializes only the structural data needed to reconstruct
/// the outbox (sender, epoch, sequence numbers, items). Internal
/// bookkeeping fields (fingerprint cache, time provider) are excluded.
/// </para>
/// </remarks>
internal sealed class OutboxJsonConverterFactory : JsonConverterFactory
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
