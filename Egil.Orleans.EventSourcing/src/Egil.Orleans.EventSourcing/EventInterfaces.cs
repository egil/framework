namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Base interface for events that support deduplication.
/// </summary>
public interface IDeduplicatedEvent
{
    /// <summary>
    /// Gets the unique identifier for deduplication purposes.
    /// </summary>
    string DeduplicationId { get; }
}

/// <summary>
/// Base interface for events that have an explicit stream target.
/// </summary>
public interface ITargetedEvent
{
    /// <summary>
    /// Gets the target stream name for this event.
    /// </summary>
    string TargetStream { get; }
}

/// <summary>
/// Marker interface for events that should be published to the outbox.
/// </summary>
public interface IPublishableEvent
{
}

/// <summary>
/// Attribute to configure event stream mapping.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class EventStreamAttribute : Attribute
{
    public string StreamName { get; }
    public bool EnableDeduplication { get; set; }
    public Type? RetentionPolicyType { get; set; }

    public EventStreamAttribute(string streamName)
    {
        StreamName = streamName;
    }
}
