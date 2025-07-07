namespace Egil.Orleans.EventSourcing.Examples;

[Immutable, GenerateSerializer, Alias("UserMessage.V1")]
public sealed record UserMessage(string UserId, string Message, DateTimeOffset Timestamp);
