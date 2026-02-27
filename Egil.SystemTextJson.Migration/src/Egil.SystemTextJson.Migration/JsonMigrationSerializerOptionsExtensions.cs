using System.Text.Json;
using Egil.SystemTextJson.Migration.Migrations;

namespace Egil.SystemTextJson.Migration;

/// <summary>
/// Adds migration support to <see cref="JsonSerializerOptions"/>.
/// </summary>
public static class JsonMigrationSerializerOptionsExtensions
{
    /// <summary>
    /// Adds migration support using explicit registration and optional assembly scanning.
    /// </summary>
    /// <param name="options">The serializer options to configure.</param>
    /// <param name="configure">Optional registration callback.</param>
    /// <returns>The same <paramref name="options"/> instance.</returns>
    public static JsonSerializerOptions AddJsonMigrationSupport(
        this JsonSerializerOptions options,
        Action<JsonMigrationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var builder = new JsonMigrationBuilder();
        configure?.Invoke(builder);

        var registry = builder.Build();
        options.Converters.Add(new JsonMigratableConverterFactory(registry));
        return options;
    }
}
