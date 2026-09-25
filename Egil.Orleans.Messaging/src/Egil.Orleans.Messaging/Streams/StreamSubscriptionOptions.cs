namespace Egil.Orleans.Messaging.Streams;

/// <summary>
/// Settings for one <see cref="StreamManager"/> subscription.
/// </summary>
/// <remarks>
/// <para>
/// Every subscription starts from the silo-wide defaults registered with
/// <c>ConfigureStreamManager</c> on the silo builder, or with
/// <c>services.Configure&lt;StreamSubscriptionOptions&gt;(...)</c>. The
/// <c>configure</c> callback passed to <c>ConfigureImplicitSubscription</c> or
/// <c>ConfigureExplicitSubscription</c> then receives a fresh instance holding
/// those defaults and overrides what that subscription needs.
/// </para>
/// <para>
/// <see cref="StreamManager"/> reads the settings once, when the callback
/// returns. Changing the instance afterwards has no effect.
/// </para>
/// </remarks>
public sealed class StreamSubscriptionOptions
{
    /// <summary>
    /// Called when the handler throws or the stream reports an error, with the
    /// stream namespace and the exception. When <see langword="null"/>, the
    /// manager logs the error. Default: <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// An exception thrown by this callback is logged and does not stop delivery
    /// of later events.
    /// </remarks>
    public Action<string, Exception>? OnError { get; set; }

    /// <summary>
    /// Whether the manager passes the tracker's last cursor token to Orleans when
    /// it attaches or resumes the subscription. Default: <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Set it to <see langword="false"/> for a subscription that must attach
    /// without a resume token, such as one in a <c>[StatelessWorker]</c> grain,
    /// where Orleans rejects any non-null token.
    /// </remarks>
    public bool UseTrackedResumeToken { get; set; } = true;

    /// <summary>
    /// How each delivery's <c>orleans.stream.process</c> span relates to the
    /// producer's trace. Default: <see cref="StreamTraceOptions.Link"/>.
    /// </summary>
    public StreamTraceOptions Trace { get; set; } = StreamTraceOptions.Link;

    /// <summary>
    /// Clock used to measure enqueue lag for
    /// <see cref="StreamTraceOptions.ParentWithinLag(TimeSpan)"/>. Default:
    /// <see cref="TimeProvider.System"/>.
    /// </summary>
    /// <remarks>
    /// Set a shared domain clock once for the silo with the
    /// <c>ConfigureStreamManager</c> overload that receives the
    /// <see cref="IServiceProvider"/>.
    /// </remarks>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}
