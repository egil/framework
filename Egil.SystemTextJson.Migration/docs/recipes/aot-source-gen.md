# AOT & Source Generation

Migration currently requires **untrimmed, non-NativeAOT deployment**, including when using a source-generated `JsonSerializerContext` or generic migrator registration. Configure the application with:

```xml
<PropertyGroup>
  <PublishTrimmed>false</PublishTrimmed>
  <PublishAot>false</PublishAot>
</PropertyGroup>
```

## Build and publish diagnostics

Both `AddJsonMigrationSupport` overloads and all migrator registration methods declare `RequiresUnreferencedCode` and `RequiresDynamicCode`. On .NET 11, the `JsonMigratableUnionTypeClassifier` constructor also declares these requirements. Its overrides keep the System.Text.Json base contract unchanged.

- Trim analysis reports **IL2026** at the consumer call: migration discovery requires members trimming may remove; publish with `PublishTrimmed=false` and `PublishAot=false`. A source-generated `JsonSerializerContext` does not remove this requirement.
- AOT analysis reports **IL3050** at the consumer call: migration creates generic converters and invokers at runtime; publish with `PublishAot=false`. Applicable trimming diagnostics are also reported.

`PublishTrimmed=true` enables trim analysis; it does **not** by itself enable AOT analysis or guarantee IL3050. `PublishAot=true` enables AOT analysis. Analysis can also be requested explicitly through `EnableTrimAnalyzer` and `EnableAotAnalyzer`. Ordinary builds without the relevant analyzers are not guaranteed to report either warning. If analyzers remain explicitly enabled for an untrimmed application, their warnings still describe these API requirements.

The diagnostics include the corrective action and an optional link to this recipe. Do not treat suppressing a warning as establishing compatibility. See [issue #256](https://github.com/egil/framework/issues/256) for the diagnostic contract and the separately scoped AOT-safe registration follow-up.

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

## Keeping the generated fast path

`AddJsonMigrationSupport()` inserts a resolver at the front of `JsonSerializerOptions.TypeInfoResolverChain`; it adds nothing to `options.Converters`. That matters for performance: System.Text.Json only uses a context's generated fast-path serializers when `options.Converters` is empty and the options match the context's `[JsonSourceGenerationOptions]`, and one entry in that list turns the fast path off for every type in the options. With the resolver in place, types outside migration keep their generated serializer. A `[JsonMigratable]` type itself, and any type whose properties reach one, is serialized through the metadata path because its contract carries the injected discriminator.

Two consequences of living in the resolver chain:

- Resolvers added after `AddJsonMigrationSupport()` serve every type that is not `[JsonMigratable]`, so the order shown above works. Replacing the chain, or assigning `TypeInfoResolver` afterwards, removes migration support unless the new resolver still delegates to the migration entry: wrapping the entry with `WithAddedModifier`, combining it with `JsonTypeInfoResolver.Combine`, or an application-defined resolver that forwards to it keeps migration working.
- A resolver that must override a `[JsonMigratable]` type has to be inserted ahead of migration (`TypeInfoResolverChain.Insert(0, ...)` after `AddJsonMigrationSupport()`). Converters in `options.Converters` keep their previous precedence: registered before `AddJsonMigrationSupport()` they win for the type, registered after they do not.

Options that have no resolver at all still serialize through reflection, as they would without the library.

## What the library does at runtime

Migration itself is driven by `static abstract` interface methods and the type metadata your `JsonSerializerContext` provides; once a type's converter has been created, the library's own read and write paths do not use reflection (System.Text.Json's converter and metadata resolution behaves as it does without the library).

Discovery does. When a converter is created for a `[JsonMigratable]` type — once per type per `JsonSerializerOptions` — the library inspects the type's `IMigrateFrom<,>` interfaces and attributes, instantiates the generic converter and migrator invokers for the discovered source/target pairs, and activates registered external migrators through the configured `IServiceProvider` or a parameterless constructor. `RegisterMigratorsFromAssembly` additionally enumerates every type in the assembly. No reflection emit is used.

## NativeAOT and trimming

**Publishing with `PublishAot` or `PublishTrimmed` is not supported yet.** JSON source generation supplies serialization metadata; it does not replace migration's interface discovery, assembly scanning, external migrator activation, or runtime generic construction. Even `RegisterMigrator<TSource, TTarget, TMigrator>()` currently enters the reflection-based invoker factory. Trimming can remove required contracts or constructors, and NativeAOT may lack code for runtime generic instantiations. Failures can include missing migrators when reading old payloads even when current payloads appear to work.

The library enables trim and AOT analyzers on both supported target frameworks without declaring `IsTrimmable` or `IsAotCompatible`. Member-preservation annotations and warning-free library analysis do not establish compatibility. A supported NativeAOT path requires separate implementation and published runtime evidence; until then, use the deployment settings above.
