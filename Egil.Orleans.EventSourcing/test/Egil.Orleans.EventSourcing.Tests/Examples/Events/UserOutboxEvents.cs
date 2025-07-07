using System.Text.Json.Serialization;

namespace Egil.Orleans.EventSourcing.Examples.Events;

[JsonDerivedType(typeof(OffensiveLanguageDetectedEvent), "OffensiveLanguageDetectedEvent.V1")]
[JsonDerivedType(typeof(UserWelcomeEvent), "UserWelcomeEvent.V1")]
public interface IUserOutboxEvent;

[Immutable, GenerateSerializer, Alias("OffensiveLanguageDetectedEvent.V1")]
public sealed record OffensiveLanguageDetectedEvent(string UserId, string Message, DateTimeOffset Timestamp)
    : IUserOutboxEvent;

[Immutable, GenerateSerializer, Alias("UserWelcomeEvent.V1")]
public sealed record UserWelcomeEvent(string Email, string Name, DateTimeOffset Timestamp)
    : IUserOutboxEvent;