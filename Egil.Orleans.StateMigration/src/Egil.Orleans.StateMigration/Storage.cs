using System.Text.Json.Serialization;

namespace Egil.Orleans.StateMigration;

[JsonConverter(typeof(StorageJsonConverterFactory))]
public sealed class Storage<TStateType>
{
    public required TStateType State { get; init; }

    public bool MigratedDuringDeserialization { get; init; }
}
