namespace Egil.SystemTextJson.Migration;

/// <summary>
/// Controls what happens when a matched migrator returns <c>false</c> from <c>TryMigrateFrom</c>.
/// </summary>
public enum JsonMigrationFailureHandling
{
    /// <summary>
    /// Throw a <see cref="System.Text.Json.JsonException"/> when migration fails.
    /// </summary>
    ThrowJsonException = 0,

    /// <summary>
    /// Fallback to deserializing the payload directly as the target type.
    /// </summary>
    FallBackToTargetType = 1,

    /// <summary>
    /// Return <c>null</c> when migration fails.
    /// </summary>
    ReturnNull = 2,
}
