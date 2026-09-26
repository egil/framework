namespace Egil.Orleans.Messaging.Tracking;

/// <summary>
/// Holds the silo-wide clock that <see cref="MessageTracker"/> falls back to when
/// an instance has no clock of its own.
/// </summary>
/// <remarks>
/// A tracker is a persisted value with no access to the silo's services, and it is
/// created by grain code, Orleans serialization, and JSON converters alike, so the
/// fallback cannot come from dependency injection. Only
/// <c>ConfigureMessageTracker</c> on the silo builder sets it. The value is
/// process-wide: each running silo that configured a clock owns one entry, and the
/// most recently started of them wins. A silo that stops removes only its own
/// entry, so silos stopping out of order never leave a stopped silo's clock, or
/// no clock, behind while another is still running.
/// </remarks>
internal static class MessageTrackerClock
{
    private static readonly Lock Gate = new();
    private static readonly List<(object Owner, TimeProvider Clock)> Installed = [];
    private static volatile TimeProvider? siloDefault;

    internal static TimeProvider Resolve(TimeProvider? own) => own ?? siloDefault ?? TimeProvider.System;

    internal static void Install(object owner, TimeProvider clock)
    {
        lock (Gate)
        {
            Installed.Add((owner, clock));
            siloDefault = clock;
        }
    }

    internal static void Uninstall(object owner)
    {
        lock (Gate)
        {
            Installed.RemoveAll(entry => ReferenceEquals(entry.Owner, owner));
            siloDefault = Installed.Count > 0 ? Installed[^1].Clock : null;
        }
    }
}
