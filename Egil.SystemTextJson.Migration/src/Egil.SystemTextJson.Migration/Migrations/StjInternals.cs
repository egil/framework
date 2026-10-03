using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
#if NET8_0
using System.Text.Json.Serialization.Metadata;
#endif

namespace Egil.SystemTextJson.Migration.Migrations;

/// <summary>
/// Provides access to internal System.Text.Json collection metadata and converter
/// entry points, including a path that bypasses the <c>GetReaderScopedToNextValue</c>
/// overhead in <c>JsonSerializer.Deserialize</c>.
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
/// Targeted internal APIs:
/// - <c>JsonConverter.ReadAsObject(ref Utf8JsonReader, Type, JsonSerializerOptions)</c>
///   on .NET 8, .NET 9, .NET 10 and .NET 11; the signature was verified unchanged
///   through the v11.0.0-rc.1 source.
/// - <c>JsonTypeInfo.get_ElementType()</c> on .NET 8 only; this property is internal
///   in System.Text.Json 8 and public from System.Text.Json 9 onward.
///
/// A signature change in a future runtime surfaces as a <see cref="MissingMethodException"/>
/// on first use; the test suite exercises each accessor on its applicable target frameworks.
/// </summary>
internal static class StjInternals
{
#if NET8_0
    // ElementType is internal in STJ 8 and public from STJ 9 onward. Read the actual
    // resolved contract rather than inferring its element from CLR interfaces: custom
    // metadata can select a different collection contract than the default resolver.
    // Keeping this accessor in the net8.0 asset also avoids upgrading JSON inside hosts
    // that already loaded STJ 8 (for example, AutoCAD/Civil 3D).
    // TODO: Remove this accessor when the net8.0 target is retired.
    // https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/Metadata/JsonTypeInfo.cs
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_ElementType")]
    internal static extern Type? GetElementType(JsonTypeInfo @this);
#endif

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
