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
/// process-wide: silos sharing a process share the clock of the last silo to start.
/// </remarks>
internal static class MessageTrackerClock
{
    private static volatile TimeProvider? siloDefault;

    internal static TimeProvider? SiloDefault => siloDefault;

    internal static TimeProvider Resolve(TimeProvider? own) => own ?? siloDefault ?? TimeProvider.System;

    internal static void Install(TimeProvider? value) => siloDefault = value;
}
