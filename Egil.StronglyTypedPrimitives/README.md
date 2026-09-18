# Strongly Typed Primitives

A source generator for creating strongly-typed primitive types that makes
it easy to avoid the primitive obsession anti pattern.

## Features

- **Ensure that a strongly-typed primitive is always valid** (per `IsValueValid` method), **or** equal to `Empty`.

- **Any generated method or property can be overridden by the user**. Don't like the generated code, just declare the method or property in the type and the generator will not generate it.

- **Constraints from DataAnnotations attributes**. Declare `System.ComponentModel.DataAnnotations` validation attributes on the positional parameter, for example `[EmailAddress, StringLength(254)] string Value`, and the generator writes `IsValueValid` from them. See [Getting started](#getting-started).

- **ASP.NET Core validation**. Declare `IValidatableObject` on the partial declaration and the generator writes `Validate` from the constraints, so a hand-written `IsValueValid` is reported by ASP.NET Core validation as a 400 instead of slipping through. See [ASP.NET Core validation](#aspnet-core-validation).

- **Interoperable with other source generators**. The "Value" property is visible to them and can be used in their generated code.

- **Generates implementation of the following interfaces**, if the underlying type supports them:

  - `System.IParsable<TSelf>`
  - `System.ISpanParsable<TSelf>`
  - `System.IUtf8SpanParsable<TSelf>`
  - `System.IComparable<TSelf>`
  - `System.IComparable`
  - `System.IFormattable`
  - `System.ISpanFormattable`
  - `System.IUtf8SpanFormattable` 

- **Supported primitive types (among others)**:

  - `string`
  - `int`
  - `decimal`
  - `long`
  - `double`
  - `Guid`
  - `DateTime`
  - `DateTimeOffset`
  - `TimeOnly`
  - `DateOnly`
  - `TimeSpan`
  - `byte`
    
- All types are marked with `IStronglyTypedPrimitive`, `IStronglyTypedPrimitive<TPrimitiveType>` and `IStronglyTypedPrimitive<TSelf, TPrimitiveType>`.

- **System.Text.Json support**. Every type is declared with a `[JsonConverter]` pointing at the shared, trim/AOT-safe `StronglyTypedJsonConverter<TSelf, TPrimitiveType>`, if the target type is in an assembly that references `System.Text.Json` and the type does not already have a `JsonConverter` attribute declared on it. The converter serializes the type as its primitive value, also when used as a dictionary key, and dictionary keys are culture invariant. See [System.Text.Json](#systemtextjson) for how to use it with a `JsonSerializerContext`.

- **.NET 9 OpenAPI support**. The library includes a custom schema transformer that will ensure strongly typed types have the right OpenAPI schema definition.

## Getting started

To get started, download the nuget [StronglyTypedPrimitives](https://www.nuget.org/packages/StronglyTypedPrimitives) and
add a `[StronglyTyped]` attribute to a **partial record struct** that has one of the 
supported primitive types as the first (and only) argument in it's constructor, for example:

```csharp
using Egil.StronglyTypedPrimitives;

namespace Examples;

[StronglyTyped]
public readonly partial record struct StronglyTypedInt(int Value);
```

To constrain what values are legal for an strongly-typed primitive, implement the `IsValueValid` method:

```csharp
using Egil.StronglyTypedPrimitives;

namespace Examples;

[StronglyTyped]
public readonly partial record struct StronglyTypedIntWithConstraints(int Value)
{
    public static bool IsValueValid(int value, bool throwIfInvalid)
    {
        if (value > 5)
            return true;

        if (throwIfInvalid)
            throw new ArgumentException("Value must be at larger than 5", nameof(value));

        return false;
    }
}
```

The generated type will ensure that the value is always valid or `Empty`, i.e.:

```csharp
var tooLowValue = 5;
var goodValue = 6;

// The default value for a stringly typed primitive is the same as `Empty`.
// This makes it easy to test if an instance is valid or not.
Assert.Equal(StronglyTypedIntWithConstraints.Empty, default(StronglyTypedIntWithConstraints));

// Creating an instance with an invalid value results in an exception, both
// when instantiating an new instance of when cloning/with'ing the record.
Assert.Throws<ArgumentException>(() => new StronglyTypedIntWithConstraints(tooLowValue));
Assert.Throws<ArgumentException>(() => StronglyTypedIntWithConstraints.Empty with { Value = tooLowValue });
```

Instead of writing `IsValueValid` by hand, declare validation attributes from `System.ComponentModel.DataAnnotations` on the positional parameter and the generator writes the method for you:

```csharp
using System.ComponentModel.DataAnnotations;
using Egil.StronglyTypedPrimitives;

namespace Examples;

[StronglyTyped]
public readonly partial record struct Email([EmailAddress, StringLength(254, MinimumLength = 3)] string Value);
```

Every attribute deriving from `ValidationAttribute` that targets the parameter itself is evaluated in declaration order. An invalid value throws a `ValidationException` whose message lists the error message of every failing attribute, one per line, and whose `Value` is the rejected value; `TryParse` and JSON deserialization return `false`/`Empty` as with a hand-written `IsValueValid`. See [Generator output for string with validation attributes](#generator-output-for-string-with-validation-attributes) for the generated code.

A hand-written `IsValueValid` still wins: when the type declares the method, the attributes are not evaluated and the generator reports warning `STP002` on each of them. Attributes deriving from `AsyncValidationAttribute` (.NET 11) cannot run inside the synchronous `IsValueValid` and are left out with warning `STP003`. Attributes that override `RequiresValidationContext`, such as `CustomValidation`, need a `ValidationContext` that `IsValueValid` does not have; they are left out as well with warning `STP005`.

## System.Text.Json

How a strongly typed primitive is serialized depends on how you use `System.Text.Json`:

1. **Reflection-based serialization** (`JsonSerializer.Serialize(value)` without a context): nothing to do. The generated `[JsonConverter]` attribute is picked up automatically.

2. **`JsonSerializerContext` on a JIT runtime**: the `System.Text.Json` source generator cannot see attributes emitted by other source generators, so it would serialize the type as an object (`{"Value":7}`). Register the factory in the options and the context will use the shared converter for every strongly typed primitive:

   ```csharp
   var options = new JsonSerializerOptions
   {
       TypeInfoResolver = AppJsonContext.Default,
       Converters = { new StronglyTypedJsonConverterFactory() },
   };
   ```

3. **`JsonSerializerContext` with trimming or Native AOT**: the factory closes the generic converter with reflection, so declare the converter on your own partial declaration instead. The generator then emits nothing JSON-related for that type:

   ```csharp
   [StronglyTyped]
   [JsonConverter(typeof(StronglyTypedJsonConverter<StronglyTypedInt, int>))]
   public readonly partial record struct StronglyTypedInt(int Value);
   ```

The generator reports warning `STP001` for every strongly typed primitive without a user-declared `[JsonConverter]` when the compilation also contains a `JsonSerializerContext`, so the object-shaped output from option 2 and 3 does not go unnoticed.

### Compatibility

`StronglyTypedJsonConverter<TSelf, TPrimitiveType>` and `StronglyTypedJsonConverterFactory` ship in the `net8.0`, `net9.0` and `net10.0` assets of the package only. Projects that pick the `netstandard2.0` asset (for example .NET Framework or .NET Standard class libraries) get no generated JSON support, even when they reference `System.Text.Json`: the generator emits no `[JsonConverter]` attribute for them and reports warning `STP004` instead. Declare your own `[JsonConverter]` on the partial declaration to serialize the type there, or target `net8.0` or later. Versions before 2.0 generated a nested converter for every target framework.

## ASP.NET Core validation

An invalid value in a request body is deserialized to `Empty` by the JSON converter rather than failing the request, so the bound object carries the default value and only validation can reject it. A route or query value that fails `TryParse` is different: that is a binding failure, and ASP.NET Core answers 400 before the handler runs, with an empty body. When validation is registered (`builder.Services.AddValidation()`, .NET 10) the endpoint filter pipeline still runs with the parameter at its default, so that 400 carries the validation problem details for `Empty` instead. Which constraints validation sees depends on how they are expressed, because its validation source generator only sees your own declarations, never the partial this generator adds:

- **Validation attributes** on the positional parameter are visible to it on the `Value` property, so it validates them itself. An invalid `Quantity` property of type `StronglyTypedQuantity([Range(6, 100)] int Value)` is reported under `Quantity.Value` with the attribute's message. Nothing extra is needed.

- **A hand-written `IsValueValid`** is invisible to it, so the type is not validated at all. To fix that, declare `System.ComponentModel.DataAnnotations.IValidatableObject` on your partial declaration:

  ```csharp
  [StronglyTyped]
  public readonly partial record struct StronglyTypedIntWithConstraints(int Value) : IValidatableObject
  {
      public static bool IsValueValid(int value, bool throwIfInvalid)
      {
          if (value > 5)
              return true;

          if (throwIfInvalid)
              throw new ArgumentException("Value must be larger than 5", nameof(value));

          return false;
      }
  }
  ```

  The generator fills in `Validate`, which calls `IsValueValid(Value, throwIfInvalid: true)` and reports the message of the `ArgumentException` or `ValidationException` it throws as a `ValidationResult` (or `"Value is not valid."` when the method returns `false` without throwing). Because the runtime calls `Validate` without a member name, .NET 10 records the result under the empty key in the problem details.

The generator never adds `IValidatableObject` on its own: the validation source generator would not see it, so the declaration has to be yours. A `Validate` you write yourself, implicitly or as an explicit interface implementation, is left alone like every other generated member. `Validate` is generated for every type that declares the interface, so a type with validation attributes gets one that evaluates the attributes, which is what `Validator.TryValidateObject` and other `IValidatableObject` consumers see. Given:

```csharp
using System.ComponentModel.DataAnnotations;
using Egil.StronglyTypedPrimitives;

namespace Examples;

[StronglyTyped]
public readonly partial record struct Email([EmailAddress, StringLength(254, MinimumLength = 3)] string Value) : IValidatableObject;
```

the following `Validate` is generated next to the `IsValueValid` shown in [Generator output for string with validation attributes](#generator-output-for-string-with-validation-attributes):

```csharp
public System.Collections.Generic.IEnumerable<System.ComponentModel.DataAnnotations.ValidationResult> Validate(System.ComponentModel.DataAnnotations.ValidationContext validationContext)
{
    var result0 = valueValidator0.GetValidationResult(Value, validationContext);
    if (result0 == System.ComponentModel.DataAnnotations.ValidationResult.Success) result0 = null;
    var result1 = valueValidator1.GetValidationResult(Value, validationContext);
    if (result1 == System.ComponentModel.DataAnnotations.ValidationResult.Success) result1 = null;

    if (result0 is null && result1 is null) return System.Array.Empty<System.ComponentModel.DataAnnotations.ValidationResult>();

    var results = new System.ComponentModel.DataAnnotations.ValidationResult[(result0 is null ? 0 : 1) + (result1 is null ? 0 : 1)];
    var index = 0;
    if (result0 is not null) results[index++] = result0;
    if (result1 is not null) results[index++] = result1;
    return results;
}
```

Every attribute is evaluated so one result per failing attribute is returned, and a valid value returns an empty array without allocating. Attributes deriving from `AsyncValidationAttribute` are not part of `Validate`. In ASP.NET Core validation the attribute errors on `Value` take precedence: `Validate` is only consulted when no member has failed, so the two never report the same failure twice.
## Generator output for int without constraints

Given this type declaration:

```csharp
using Egil.StronglyTypedPrimitives;

namespace Examples;

[StronglyTyped]
public readonly partial record struct StronglyTypedInt(int Value);
```

The following code is generated:

```csharp
#nullable enable

namespace Examples;

[System.CodeDom.Compiler.GeneratedCodeAttribute("Egil.StronglyTypedPrimitives, Version=1.14.0.0, Culture=neutral, PublicKeyToken=null", "1.14.0.0")]
[System.Text.Json.Serialization.JsonConverterAttribute(typeof(Egil.StronglyTypedPrimitives.StronglyTypedJsonConverter<Examples.StronglyTypedInt, int>))]
public readonly partial record struct StronglyTypedInt : Egil.StronglyTypedPrimitives.IStronglyTypedPrimitive<int>, Egil.StronglyTypedPrimitives.IStronglyTypedPrimitive<Examples.StronglyTypedInt, int>, System.IParsable<Examples.StronglyTypedInt>, System.ISpanParsable<Examples.StronglyTypedInt>, System.IUtf8SpanParsable<Examples.StronglyTypedInt>, System.IComparable<Examples.StronglyTypedInt>, System.IComparable, System.IFormattable, System.ISpanFormattable, System.IUtf8SpanFormattable
{
    public static readonly StronglyTypedInt Empty = default;

    public static StronglyTypedInt Create(int value) => new StronglyTypedInt(value);

    public override string ToString() => Value.ToString();

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static bool IsValueValid(int value, bool throwIfInvalid)
        => true;

    public static StronglyTypedInt Parse(string s, System.IFormatProvider? provider)
    {
        var rawValue = int.Parse(s, provider);
        IsValueValid(rawValue, throwIfInvalid: true);
        return new StronglyTypedInt(rawValue);
    }

    public static bool TryParse(string? s, System.IFormatProvider? provider, [System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(returnValue: false)] out Examples.StronglyTypedInt result)
    {
        if (int.TryParse(s, provider, out var rawValue) && IsValueValid(rawValue, throwIfInvalid: false))
        {
            result = new StronglyTypedInt(rawValue);
            return true;
        }

        result = StronglyTypedInt.Empty;
        return false;
    }

    public static StronglyTypedInt Parse(System.ReadOnlySpan<char> s, System.IFormatProvider? provider)
    {
        var rawValue = int.Parse(s, provider);
        IsValueValid(rawValue, throwIfInvalid: true);
        return new StronglyTypedInt(rawValue);
    }

    public static bool TryParse(System.ReadOnlySpan<char> s, System.IFormatProvider? provider, [System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(returnValue: false)] out Examples.StronglyTypedInt result)
    {
        if (int.TryParse(s, provider, out var rawValue) && IsValueValid(rawValue, throwIfInvalid: false))
        {
            result = new StronglyTypedInt(rawValue);
            return true;
        }

        result = StronglyTypedInt.Empty;
        return false;
    }

    public static StronglyTypedInt Parse(System.ReadOnlySpan<byte> utf8Text, System.IFormatProvider? provider)
    {
        var rawValue = int.Parse(utf8Text, provider);
        IsValueValid(rawValue, throwIfInvalid: true);
        return new StronglyTypedInt(rawValue);
    }

    public static bool TryParse(System.ReadOnlySpan<byte> utf8Text, System.IFormatProvider? provider, [System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(returnValue: false)] out Examples.StronglyTypedInt result)
    {
        if (int.TryParse(utf8Text, provider, out var rawValue) && IsValueValid(rawValue, throwIfInvalid: false))
        {
            result = new StronglyTypedInt(rawValue);
            return true;
        }

        result = StronglyTypedInt.Empty;
        return false;
    }
    
    public int CompareTo(Examples.StronglyTypedInt other)
        => Value.CompareTo(other.Value);
    
    public int CompareTo(object? obj)
    {
        if (obj is null)
        {
            return 1;
        }

        if (obj is StronglyTypedInt other)
        {
            return Value.CompareTo(other.Value);
        }

        return ((System.IComparable)Value).CompareTo(obj);
    }
    
    public string ToString(string? format, System.IFormatProvider? formatProvider)
        => Value.ToString(format, formatProvider);
    
    public bool TryFormat(System.Span<char> destination, out int charsWritten, System.ReadOnlySpan<char> format, System.IFormatProvider? provider)
        => ((System.ISpanFormattable)Value).TryFormat(destination, out charsWritten, format, provider);
    
    public bool TryFormat(System.Span<byte> utf8Destination, out int bytesWritten, System.ReadOnlySpan<char> format, System.IFormatProvider? provider)
        => ((System.IUtf8SpanFormattable)Value).TryFormat(utf8Destination, out bytesWritten, format, provider);
    
    public static bool operator > (StronglyTypedInt a, StronglyTypedInt b) => a.CompareTo(b) > 0;                

    public static bool operator < (StronglyTypedInt a, StronglyTypedInt b) => a.CompareTo(b) < 0;                

    public static bool operator >=(StronglyTypedInt a, StronglyTypedInt b) => a.CompareTo(b) >= 0;
    
    public static bool operator <=(StronglyTypedInt a, StronglyTypedInt b) => a.CompareTo(b) <= 0;
}
```

When the positional parameter is not named `Value`, an explicit implementation of `IStronglyTypedPrimitive<TSelf, TPrimitiveType>.Value` is generated as well, for example `int Egil.StronglyTypedPrimitives.IStronglyTypedPrimitive<Examples.StronglyTypedInt, int>.Value => Data;`.

See more examples in https://github.com/egil/framework/tree/main/Egil.StronglyTypedPrimitives/test/Egil.StronglyTypedPrimitives.Tests

## Generator output for int with constraints

Given this type declaration:

```csharp
using Egil.StronglyTypedPrimitives;

namespace Examples;

[StronglyTyped]
public readonly partial record struct StronglyTypedIntWithConstraints(int Value)
{
    public static bool IsValueValid(int value, bool throwIfInvalid)
    {
        if (value > 5)
            return true;

        if (throwIfInvalid)
            throw new ArgumentException("Value must be at larger than 5", nameof(value));

        return false;
    }
}
```

The following code is generated when using C# 13 or below:

```csharp
#nullable enable

namespace Examples;

[System.CodeDom.Compiler.GeneratedCodeAttribute("Egil.StronglyTypedPrimitives, Version=1.14.0.0, Culture=neutral, PublicKeyToken=null", "1.14.0.0")]
[System.Text.Json.Serialization.JsonConverterAttribute(typeof(Egil.StronglyTypedPrimitives.StronglyTypedJsonConverter<Examples.StronglyTypedIntWithConstraints, int>))]
public readonly partial record struct StronglyTypedIntWithConstraints : Egil.StronglyTypedPrimitives.IStronglyTypedPrimitive<int>, Egil.StronglyTypedPrimitives.IStronglyTypedPrimitive<Examples.StronglyTypedIntWithConstraints, int>, System.IParsable<Examples.StronglyTypedIntWithConstraints>, System.ISpanParsable<Examples.StronglyTypedIntWithConstraints>, System.IUtf8SpanParsable<Examples.StronglyTypedIntWithConstraints>, System.IComparable<Examples.StronglyTypedIntWithConstraints>, System.IComparable, System.IFormattable, System.ISpanFormattable, System.IUtf8SpanFormattable
{
    public static readonly StronglyTypedIntWithConstraints Empty = default;

    public static StronglyTypedIntWithConstraints Create(int value) => new StronglyTypedIntWithConstraints(value);

    private static int ThrowIfValueIsInvalid(int value)
    {
        IsValueValid(value, throwIfInvalid: true);
        return value;
    }

    private readonly int @value = ThrowIfValueIsInvalid(Value);

    public int Value
    {
        get => @value;
        init
        {
            @value = ThrowIfValueIsInvalid(value);
        }
    }
    
    // remaining cut for brevity. same as first example above ...
}
```

The following code is generated when using C# 14 or higher:

```csharp
#nullable enable

namespace Examples;

[System.CodeDom.Compiler.GeneratedCodeAttribute("Egil.StronglyTypedPrimitives, Version=1.14.0.0, Culture=neutral, PublicKeyToken=null", "1.14.0.0")]
[System.Text.Json.Serialization.JsonConverterAttribute(typeof(Egil.StronglyTypedPrimitives.StronglyTypedJsonConverter<Examples.StronglyTypedIntWithConstraints, int>))]
public readonly partial record struct StronglyTypedIntWithConstraints : Egil.StronglyTypedPrimitives.IStronglyTypedPrimitive<int>, Egil.StronglyTypedPrimitives.IStronglyTypedPrimitive<Examples.StronglyTypedIntWithConstraints, int>, System.IParsable<Examples.StronglyTypedIntWithConstraints>, System.ISpanParsable<Examples.StronglyTypedIntWithConstraints>, System.IUtf8SpanParsable<Examples.StronglyTypedIntWithConstraints>, System.IComparable<Examples.StronglyTypedIntWithConstraints>, System.IComparable, System.IFormattable, System.ISpanFormattable, System.IUtf8SpanFormattable
{
    public static readonly StronglyTypedIntWithConstraints Empty = default;

    public static StronglyTypedIntWithConstraints Create(int value) => new StronglyTypedIntWithConstraints(value);

    private static int ThrowIfValueIsInvalid(int value)
    {
        IsValueValid(value, throwIfInvalid: true);
        return value;
    }

    public int Value
    {
        get => field;
        init
        {
            field = ThrowIfValueIsInvalid(value);
        }
    } = ThrowIfValueIsInvalid(Value);

    // remaining cut for brevity. same as first example above ...
}
```

## Generator output for string with validation attributes

Given this type declaration:

```csharp
using System.ComponentModel.DataAnnotations;
using Egil.StronglyTypedPrimitives;

namespace Examples;

[StronglyTyped]
public readonly partial record struct Email([EmailAddress, StringLength(254, MinimumLength = 3)] string Value);
```

The `Value` property and `ThrowIfValueIsInvalid` are generated exactly as in the constraints example above, and `IsValueValid` is generated from the attributes:

```csharp
private static class ValueValidators
{
    public static readonly global::System.ComponentModel.DataAnnotations.EmailAddressAttribute valueValidator0 = new global::System.ComponentModel.DataAnnotations.EmailAddressAttribute();
    public static readonly global::System.ComponentModel.DataAnnotations.StringLengthAttribute valueValidator1 = new global::System.ComponentModel.DataAnnotations.StringLengthAttribute(254) { MinimumLength = 3 };

    public static global::System.ComponentModel.DataAnnotations.ValidationContext CreateInvariantContext()
        => new global::System.ComponentModel.DataAnnotations.ValidationContext(new object(), "Value", null, null) { MemberName = "Value" };
}

public static bool IsValueValid(string value, bool throwIfInvalid)
{
    var context = ValueValidators.CreateInvariantContext();
    string? error0 = null;
    string? error1 = null;

    if (ValueValidators.valueValidator0.GetValidationResult(value, context) is { } result0)
    {
        if (!throwIfInvalid) return false;
        error0 = result0.ErrorMessage ?? "The field Value is invalid.";
    }

    if (ValueValidators.valueValidator1.GetValidationResult(value, context) is { } result1)
    {
        if (!throwIfInvalid) return false;
        error1 = result1.ErrorMessage ?? "The field Value is invalid.";
    }

    if (error0 is null && error1 is null) return true;

    var message = string.Empty;
    if (error0 is not null) message = error0;
    if (error1 is not null) message = message.Length == 0 ? error1 : message + global::System.Environment.NewLine + error1;
    throw new global::System.ComponentModel.DataAnnotations.ValidationException(message, null, value);
}
```

With `throwIfInvalid: false` the method returns at the first failing attribute. With `throwIfInvalid: true` every attribute is evaluated so the exception reports all of them at once. Every attribute is evaluated through `GetValidationResult` with a `ValidationContext` created for that call, whose `MemberName` and `DisplayName` are the name of the positional parameter and whose `ObjectInstance` is a placeholder object, so attributes that override either `IsValid` overload work, and error messages come out formatted with the parameter name. A failing attribute that produces no message at all (a `ValidationResult` with a null `ErrorMessage` and a `FormatErrorMessage` that returns null) is still a failure, reported with DataAnnotations' default wording `The field Value is invalid.`. The context is not shared between calls because it is mutable and an attribute may write to its `Items`; the cost is one small allocation per validated construction or parse of an attribute-constrained type, and none for types without attributes. Before .NET 10 `ValidationContext` has no trim-safe constructor, so on those targets `CreateInvariantContext` calls `ValidationContext(object)` with `DisplayName` set (which keeps its reflection fallback from running) and carries an `UnconditionalSuppressMessage` for IL2026. The attribute instances live in a nested `ValueValidators` class (suffixed with underscores if the type already has a member of that name) so that they are initialized on first use, even from a static initializer on the type itself such as `public static readonly Email Default = new("a@b.c");`.

## .NET 9 OpenAPI support

The library includes a custom schema transformer that will ensure strongly typed types have the right OpenAPI schema definition. To use it, add the following to your OpenApi options:

```csharp
using Egil.StronglyTypedPrimitives;

builder.Services.AddOpenApi(options =>
{
    options.AddSchemaTransformer<StronglyTypedSchemaTransformer>();
}
```

## Alternatives

There are other alternatives to this source generator that you can consider if you need something different:

- [StronglyTypedId by Andrew Lock](https://github.com/andrewlock/StronglyTypedId)
- [Meziantou.Framework.StronglyTypedId by Gérald Barré (meziantou)](https://www.nuget.org/packages/Meziantou.Framework.StronglyTypedId)

Both are excellent and I have used them in the past.