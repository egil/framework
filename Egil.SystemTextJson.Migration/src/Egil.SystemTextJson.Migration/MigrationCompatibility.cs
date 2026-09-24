namespace Egil.SystemTextJson.Migration;

internal static class MigrationCompatibility
{
    internal const string Trimming = "Migration discovery requires members that trimming may remove. "
        + "Publish this application with PublishTrimmed=false and PublishAot=false. "
        + "A source-generated JsonSerializerContext does not remove this requirement.";

    internal const string DynamicCode = "Migration creates generic converters and invokers at runtime. "
        + "Publish this application with PublishAot=false. "
        + "A source-generated JsonSerializerContext does not remove this requirement.";

    internal const string Url = "https://github.com/egil/framework/blob/main/Egil.SystemTextJson.Migration/docs/recipes/aot-source-gen.md";
}
