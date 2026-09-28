namespace Egil.Orleans.Messaging.Tracking;

/// <summary>
/// Holds the silo-wide settings that <see cref="MessageTracker"/> falls back to when
/// an instance has no configuration of its own.
/// </summary>
/// <remarks>
/// A tracker is a persisted value with no access to the silo's services, and it is
/// created by grain code, Orleans serialization, and JSON converters alike, so the
/// fallback is installed by <c>ConfigureMessageTracker</c> before grains activate.
/// The value is process-wide: the most recently started configured silo wins.
/// Each silo removes only its own entry, preserving other running silos' defaults.
/// </remarks>
internal static class MessageTrackerDefaults
{
    private static readonly Lock Gate = new();
    private static readonly List<(object Owner, MessageTrackerSettings Settings)> Installed = [];
    private static volatile MessageTrackerSettings siloDefault = MessageTrackerSettings.Default;

    internal static MessageTrackerSettings Current => siloDefault;

    internal static void Install(object owner, MessageTrackerSettings settings)
    {
        lock (Gate)
        {
            Installed.Add((owner, settings));
            siloDefault = settings;
        }
    }

    internal static void Uninstall(object owner)
    {
        lock (Gate)
        {
            Installed.RemoveAll(entry => ReferenceEquals(entry.Owner, owner));
            siloDefault = Installed.Count > 0 ? Installed[^1].Settings : MessageTrackerSettings.Default;
        }
    }
}
