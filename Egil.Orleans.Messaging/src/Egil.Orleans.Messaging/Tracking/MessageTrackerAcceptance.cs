using Egil.Orleans.Messaging.Streams;

namespace Egil.Orleans.Messaging.Tracking;

// Journaling records the resolved cursor and cutoff, rather than evaluating configuration
// again during recovery. This also covers tokenless receives that only remove expired state.
internal readonly record struct MessageTrackerAcceptance(DateTimeOffset Received, DateTimeOffset? RetentionCutoff, StreamCursor? Stream = null);
