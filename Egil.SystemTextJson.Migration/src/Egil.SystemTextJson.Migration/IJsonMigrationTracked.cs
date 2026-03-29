namespace Egil.SystemTextJson.Migration;

/// <summary>
/// Tracks whether a value was migrated while being deserialized from JSON.
/// </summary>
public interface IJsonMigrationTracked
{
    /// <summary>
    /// Gets or sets whether this instance was migrated during deserialization.
    /// </summary>
    bool MigratedDuringDeserialization { get; set; }
}
