namespace Egil.Orleans.EventSourcing.Examples.Events;

// Event that has its own partition and partition rules
[Immutable, GenerateSerializer, Alias("UserMessageSent.V1")]
public sealed record UserMessageSent(string UserId, string Message, DateTimeOffset Timestamp);
