namespace Egil.Orleans.Messaging.Tracking;

// Snapshot callbacks into immutable runtime settings so retaining and mutating an options
// object cannot change the policy of a tracker or an already running silo.
internal sealed record MessageTrackerSettings(StreamTrackingMode StreamTrackingMode, TimeSpan? RetentionPeriod, TimeProvider? TimeProvider)
{
    internal static readonly MessageTrackerSettings Default = new(StreamTrackingMode.StreamPosition, null, null);

    internal static MessageTrackerSettings FromOptions(MessageTrackerOptions options, TimeProvider? fallback)
    {
        if (!Enum.IsDefined(options.StreamTrackingMode))
            throw new ArgumentOutOfRangeException(nameof(options.StreamTrackingMode));
        if (options.RetentionPeriod is { } retention && retention <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.RetentionPeriod), "Retention must be positive or null.");

        return new(options.StreamTrackingMode, options.RetentionPeriod, options.TimeProvider ?? fallback);
    }

    internal MessageTrackerSettings Configure(Action<MessageTrackerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new MessageTrackerOptions
        {
            StreamTrackingMode = StreamTrackingMode,
            RetentionPeriod = RetentionPeriod,
            TimeProvider = TimeProvider
        };
        configure(options);
        return FromOptions(options, TimeProvider);
    }

    internal DateTimeOffset? RetentionCutoff(DateTimeOffset now)
    {
        // A very long retention period can extend beyond the representable clock range.
        // Nothing can have expired then; clamping to MinValue would incorrectly evict it.
        return RetentionPeriod is { } retention && retention <= now - DateTimeOffset.MinValue
            ? now - retention
            : null;
    }
}
