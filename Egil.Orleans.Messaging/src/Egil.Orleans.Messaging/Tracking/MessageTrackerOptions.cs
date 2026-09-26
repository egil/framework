namespace Egil.Orleans.Messaging.Tracking;

/// <summary>
/// Silo-wide settings for every <see cref="MessageTracker"/> in the silo, set with
/// <c>ConfigureMessageTracker</c> on the silo builder.
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
    /// Clock that stamps <c>Received</c> on trackers without a clock of their own
    /// from <see cref="MessageTracker.RegisterTimeProvider"/>. Default:
    /// <see langword="null"/>, which uses the <see cref="System.TimeProvider"/>
    /// registered in the silo's services, or <see cref="TimeProvider.System"/>
    /// when none is registered.
    /// </summary>
    public TimeProvider? TimeProvider { get; set; }
}
