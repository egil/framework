using System.Text.Json.Serialization;

namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// Wraps a user-defined message <typeparamref name="T"/> with its
/// <see cref="OutboxSequenceToken"/>, forming the unit of storage and
/// dispatch within an <see cref="Outbox{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Immutability:</b> Envelopes are immutable records. Retry diagnostics
/// (attempt count, last exception) are <em>not</em> stored on the envelope —
/// the <see cref="OutboxProcessor{TOutbox}"/> tracks attempts in-memory,
/// keyed by <see cref="Token"/>. On grain reactivation, attempt counts
/// restart from zero.
/// </para>
/// <para>
/// <b>Serialization:</b> Decorated with <c>[GenerateSerializer]</c> for
/// Orleans and <c>[JsonConverter]</c> (via <c>JsonConverterFactory</c>) for
/// System.Text.Json. The factory creates a closed
/// <c>JsonConverter&lt;OutboxMessageEnvelope&lt;T&gt;&gt;</c> for the specific
/// <typeparamref name="T"/> at runtime.
/// </para>
/// <para>
/// <b>Storage co-location:</b> Envelopes live inside <see cref="Outbox{T}"/>,
/// which lives inside the grain's state record. They are serialized as part
/// of the grain's single <c>WriteStateAsync</c> call — there is no separate
/// "outbox store."
/// </para>
/// </remarks>
/// <typeparam name="T">
/// The user-defined message payload type. Must be serializable by both Orleans
/// (<c>[GenerateSerializer]</c>) and System.Text.Json if the storage provider
/// uses STJ.
/// </typeparam>
[GenerateSerializer]
[Alias("egil.orleans.messaging.OutboxMessageEnvelope`1")]
[JsonConverter(typeof(OutboxMessageEnvelopeJsonConverterFactory))]
public sealed record OutboxMessageEnvelope<T>
{
    /// <summary>
    /// Creates an empty envelope for serializer use.
    /// </summary>
    [SetsRequiredMembers]
    public OutboxMessageEnvelope()
    {
        Token = new OutboxSequenceToken();
        Message = default!;
    }

    /// <summary>
    /// Creates an envelope for the given token and message payload.
    /// </summary>
    [SetsRequiredMembers]
    public OutboxMessageEnvelope(OutboxSequenceToken token, T message)
    {
        ArgumentNullException.ThrowIfNull(token);

        Token = token;
        Message = message;
    }

    /// <summary>
    /// The sequence token identifying this message. Assigned by
    /// <see cref="Outbox{T}.Add"/> — never user-constructed.
    /// </summary>
    [Id(0)]
    public required OutboxSequenceToken Token { get; init; }

    /// <summary>
    /// The user-defined message payload.
    /// </summary>
    [Id(1)]
    public required T Message { get; init; }

    [Id(2)]
    public int SendAttempts { get; internal set; }

    [JsonIgnore]
    [field: NonSerialized]
    public Exception? LastSendError { get; internal set; }
}
