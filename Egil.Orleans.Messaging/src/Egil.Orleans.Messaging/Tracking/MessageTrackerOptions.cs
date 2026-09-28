namespace Egil.Orleans.Messaging.Tracking;

/// <summary>
/// Silo-wide settings for every <see cref="MessageTracker"/> in the silo, set with
/// <c>ConfigureMessageTracker</c> on the silo builder, with per-grain overrides through
/// <see cref="MessageTracker.Configure"/>.
/// </summary>
/// <remarks>
/// The silo applies the settings when it starts, before any grain activates, and
/// withdraws them when it stops. They are process-wide: silos sharing a process, as
/// in an in-process test cluster, share the settings of the most recently started
/// silo that is still running.
/// </remarks>
public sealed class MessageTrackerOptions
{
    /// <summary>
    /// Stream duplicate detection. Default: <see cref="StreamTrackingMode.StreamPosition"/>.
    /// Explicit RPC tokens always use sender high-water marks.
    /// </summary>
    public StreamTrackingMode StreamTrackingMode { get; set; } = StreamTrackingMode.StreamPosition;

    /// <summary>
    /// Maximum age of stream checkpoints, stream receipts, and RPC sender entries, measured
    /// from receiver acceptance. Default: <see langword="null"/> (retain until manual eviction).
    /// Must be positive when set. Expired entries are removed on the next accepted message;
    /// idle trackers do no background work. Eviction ends duplicate protection for that entry.
    /// </summary>
    public TimeSpan? RetentionPeriod { get; set; }

    /// <summary>
    /// Clock that stamps <c>Received</c> on trackers without a clock of their own
    /// from <see cref="MessageTracker.RegisterTimeProvider"/>. Default:
    /// <see langword="null"/>, which uses the <see cref="System.TimeProvider"/>
    /// registered in the silo's services, or <see cref="TimeProvider.System"/>
    /// when none is registered.
    /// </summary>
    public TimeProvider? TimeProvider { get; set; }
}
