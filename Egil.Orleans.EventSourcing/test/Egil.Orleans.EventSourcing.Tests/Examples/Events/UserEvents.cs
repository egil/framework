using System.Text.Json.Serialization;

namespace Egil.Orleans.EventSourcing.Examples.Events;

// Events that share the same partition and partition rules
[JsonDerivedType(typeof(UserCreated), "UserCreated.V1")]
[JsonDerivedType(typeof(UserDeactivated), "UserDeactivated.V1")]
public interface IUserEvent;

[Immutable, GenerateSerializer, Alias("UserCreated.V1")]
public sealed record UserCreated(string UserId, string Name, string Email, DateTimeOffset Timestamp) : IUserEvent;

[Immutable, GenerateSerializer, Alias("UserDeactivated.V1")]
public sealed record UserDeactivated(string UserId, string Reason, DateTimeOffset Timestamp) : IUserEvent;