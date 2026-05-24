namespace Egil.Orleans.Messaging;

/// <summary>
/// Thrown (internally, via <see cref="OutboxProcessorOptions{TOutbox}.ReconcileFailedAsync"/>)
/// when an outbox item's runtime type does not match any registered postman.
/// </summary>
/// <remarks>
/// <para>
/// This exception is <b>never thrown out of <see cref="OutboxProcessor{TOutbox}.PostAsync"/></b>.
/// It is surfaced through the <see cref="OutboxProcessorOptions{TOutbox}.ReconcileFailedAsync"/>
/// callback as the <c>Error</c> member of the failure tuple, allowing the grain
/// to decide how to handle unmatched items (log, dead-letter, remove, etc.).
/// </para>
/// <para>
/// <b>Common cause:</b> A new subtype of the outbox base type was added but no
/// corresponding <see cref="OutboxProcessor{TOutbox}.AddPostman{TSub}(Func{TSub, ValueTask})"/>
/// call was registered. Fix by adding the missing postman registration in
/// <c>OnActivateAsync</c>.
/// </para>
/// <para>
/// <b>Postman ordering:</b> Postmen are matched first-registered-wins. If a
/// less-specific postman is registered before a more-specific one, the
/// more-specific subtype may be consumed by the less-specific postman instead
/// of reaching this exception. Register from most specific to least specific.
/// </para>
/// </remarks>
public sealed class NoPostmanRegisteredException : InvalidOperationException
{
    /// <summary>
    /// Creates a new <see cref="NoPostmanRegisteredException"/> for the
    /// given <paramref name="itemType"/>.
    /// </summary>
    /// <param name="itemType">The runtime type of the unmatched outbox item.</param>
    public NoPostmanRegisteredException(Type itemType)
        : base($"No postman registered for outbox item type '{itemType.FullName}'.")
    {
        ItemType = itemType;
    }

    /// <summary>
    /// The runtime type of the outbox item that could not be matched to any
    /// registered postman.
    /// </summary>
    public Type ItemType { get; }
}
