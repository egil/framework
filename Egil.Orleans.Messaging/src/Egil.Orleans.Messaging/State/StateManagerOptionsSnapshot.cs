namespace Egil.Orleans.Messaging.State;

/// <summary>The immutable effective configuration of a state manager.</summary>
/// <param name="RecoveryPolicy">The policy used when a storage write or clear fails.</param>
public sealed record StateManagerOptionsSnapshot(StateRecoveryPolicy RecoveryPolicy);
