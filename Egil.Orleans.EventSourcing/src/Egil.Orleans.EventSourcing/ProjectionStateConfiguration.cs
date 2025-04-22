using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing;

public readonly record struct ProjectionStateConfiguration(string StateName, string? StorageName = null) : IPersistentStateConfiguration;
