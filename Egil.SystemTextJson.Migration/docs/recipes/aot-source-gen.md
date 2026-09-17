# AOT & Source Generation

## Using with source-generated `JsonSerializerContext`

The library is compatible with System.Text.Json source generation. Register both old (source) and current (target) types in your `JsonSerializerContext`:

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
> The injected discriminator is a `string` property. A context whose registered types contain no `string` member anywhere must add `[JsonSerializable(typeof(string))]` explicitly, otherwise STJ reports that no metadata for `System.String` was provided.
>
> On .NET 11, unions of `[JsonMigratable]` cases must be annotated with `[JsonUnion(TypeClassifier = typeof(JsonMigratableUnionTypeClassifier))]` in source-generated contexts; see [polymorphism.md](polymorphism.md#recommended-on-net-11-model-the-hierarchy-as-a-union).

## What the library does at runtime

Migration itself is driven by `static abstract` interface methods and the type metadata your `JsonSerializerContext` provides; once a type's converter has been created, the library's own read and write paths do not use reflection (System.Text.Json's converter and metadata resolution behaves as it does without the library).

Discovery does. When a converter is created for a `[JsonMigratable]` type — once per type per `JsonSerializerOptions` — the library inspects the type's `IMigrateFrom<,>` interfaces and attributes, instantiates the generic converter and migrator invokers for the discovered source/target pairs, and activates registered external migrators through the configured `IServiceProvider` or a parameterless constructor. `RegisterMigratorsFromAssembly` additionally enumerates every type in the assembly. No reflection emit is used.

## NativeAOT and trimming

**Publishing with `PublishAot` or `PublishTrimmed` is not supported yet.** Source generation removes STJ's own reflection, but the trimmer also removes the `IMigrateFrom<,>` interface implementations and parameterless constructors that discovery depends on, so static migrators are silently absent and external migrators cannot be activated. A trimmed application deserializes current payloads correctly but fails old payloads with "No migrator was found for discriminator '...'". Trim-safe explicit registration is tracked as a follow-up; until it ships, run migration-enabled applications untrimmed.
