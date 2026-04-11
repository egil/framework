using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.SystemTextJson.Migration.Migrations;

/// <summary>
/// Provides access to internal System.Text.Json members that are necessary
/// to bypass the <c>GetReaderScopedToNextValue</c> overhead in
/// <c>JsonSerializer.Deserialize</c>.
///
/// When <c>JsonSerializer.Deserialize(ref reader, typeInfo)</c> is called, it
/// internally copies the reader, skips the entire JSON value to measure its span,
/// creates a new scoped reader, and then deserializes from scratch. This double-parse
/// is the primary source of the ~2x overhead compared to native STJ polymorphic
/// deserialization.
///
/// By calling the converter's <c>ReadAsObject</c> method directly, we use the
/// <c>JsonResumableConverter&lt;T&gt;.Read</c> path which creates a <c>ReadStack</c>
/// and calls <c>TryRead</c> directly — no scoped reader, no double-parse.
///
/// Targeted internal APIs (System.Text.Json, .NET 10):
/// - <c>JsonConverter.ReadAsObject(ref Utf8JsonReader, Type, JsonSerializerOptions)</c>
/// </summary>
internal static class StjInternals
{
    /// <summary>
    /// Calls the internal <c>ReadAsObject</c> method on a <see cref="JsonConverter"/>.
    /// This dispatches to <c>JsonConverter&lt;T&gt;.ReadAsObject</c> which calls
    /// the public <c>Read</c> method, bypassing <c>GetReaderScopedToNextValue</c>.
    /// </summary>
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ReadAsObject")]
    internal static extern object? ReadAsObject(
        JsonConverter @this,
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options);
}
