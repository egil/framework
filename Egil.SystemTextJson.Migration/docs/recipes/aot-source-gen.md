# AOT & Source Generation

## Using with source-generated `JsonSerializerContext`

The library is fully compatible with System.Text.Json source generation. Register both old (source) and current (target) types in your `JsonSerializerContext`:

<!-- snippet: source_gen_context -->
<a id='snippet-source_gen_context'></a>
```cs
[JsonSerializable(typeof(UserV1))]
[JsonSerializable(typeof(UserV2))]
public partial class AppJsonContext : JsonSerializerContext;
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/SourceGenSample.cs#L18-L22' title='Snippet source file'>snippet source</a> | <a href='#snippet-source_gen_context' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: source_gen_usage -->
<a id='snippet-source_gen_usage'></a>
```cs
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.AddJsonMigrationSupport();
options.TypeInfoResolverChain.Add(AppJsonContext.Default);
```
<sup><a href='/samples/Egil.SystemTextJson.Migration.Samples/SourceGenSample.cs#L29-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-source_gen_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

> **Note:** Both source and target types must be registered in the context. If a source type is missing from the context, deserialization of old payloads for that type will fail.
>
> The library itself does not use reflection for migration — it relies on `static abstract` interface methods and the type metadata provided by your `JsonSerializerContext`.
