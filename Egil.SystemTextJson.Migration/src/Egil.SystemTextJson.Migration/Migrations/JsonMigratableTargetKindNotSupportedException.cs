using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Migrations;

/// <summary>
/// Thrown when <c>[JsonMigratable]</c> is applied to a type whose JSON contract is not an object.
/// Internal subclass so the factory can tell this diagnostic apart from STJ's own
/// <see cref="NotSupportedException"/> for missing metadata, which it translates into a different message.
/// </summary>
internal sealed class JsonMigratableTargetKindNotSupportedException(Type targetType, JsonTypeInfoKind kind)
    : NotSupportedException(
        $"'{targetType.FullName}' is annotated with [{nameof(JsonMigratableAttribute).Replace("Attribute", string.Empty, StringComparison.Ordinal)}] but its JSON contract kind is '{kind}'. " +
        "Migration targets must serialize as JSON objects because the type discriminator is written as a property. " +
        "Apply the attribute to the object types instead: for unions on .NET 11 annotate the case types and let AddJsonMigrationSupport() classify the union; " +
        "for collections wrap the elements in an object type. See https://github.com/egil/framework/blob/main/Egil.SystemTextJson.Migration/docs/recipes/polymorphism.md.")
{
    public Type TargetType { get; } = targetType;

    public JsonTypeInfoKind Kind { get; } = kind;
}
