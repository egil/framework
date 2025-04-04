# Strongly Typed Primitives

A source generator for creating strongly-typed primitive types that makes
it easy to avoid the primitive obsession anti pattern.

## Features

- **Ensure that a strongly-typed primitive is always valid** (per `IsValueValid` method), **or** equal to `Empty`.

- **Any generated method or property can be overridden by the user**. Don't like the generated code, just declare the method or property in the type and the generator will not generate it.

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
    
- All types are marked with `IStronglyTypedPrimitive<TPrimitiveType>` and `IStronglyTypedPrimitive`.

- **Generates JsonConverter for System.Text.Json**, if the target type is in an assembly that references `System.Text.Json` and the type does not already have a `JsonConverter` attribute declared on it.

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

[System.CodeDom.Compiler.GeneratedCodeAttribute("Egil.StronglyTypedPrimitives, Version=1.9.0.0, Culture=neutral, PublicKeyToken=null", "1.9.0.0")]
[System.Text.Json.Serialization.JsonConverterAttribute(typeof(StronglyTypedIntJsonConverter))]
public readonly partial record struct StronglyTypedInt : Egil.StronglyTypedPrimitives.IStronglyTypedPrimitive<int>, System.IParsable<Examples.StronglyTypedInt>, System.ISpanParsable<Examples.StronglyTypedInt>, System.IUtf8SpanParsable<Examples.StronglyTypedInt>, System.IComparable<Examples.StronglyTypedInt>, System.IComparable, System.IFormattable, System.ISpanFormattable, System.IUtf8SpanFormattable
{
    public static readonly StronglyTypedInt Empty = default;

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

    public sealed class StronglyTypedIntJsonConverter : System.Text.Json.Serialization.JsonConverter<StronglyTypedInt>
    {
        public override StronglyTypedInt Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        {
            var rawValue = System.Text.Json.JsonSerializer.Deserialize<int>(ref reader, options);
            
            return StronglyTypedInt.IsValueValid(rawValue, throwIfInvalid: false)
                ? new StronglyTypedInt(rawValue)
                : StronglyTypedInt.Empty;
        }

        public override void Write(System.Text.Json.Utf8JsonWriter writer, StronglyTypedInt value, System.Text.Json.JsonSerializerOptions options)
            => System.Text.Json.JsonSerializer.Serialize(writer, value.Value, options);

        public override StronglyTypedInt ReadAsPropertyName(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
            => StronglyTypedInt.Parse(reader.GetString()!, null);

        public override void WriteAsPropertyName(System.Text.Json.Utf8JsonWriter writer, [System.Diagnostics.CodeAnalysis.DisallowNull] StronglyTypedInt value, System.Text.Json.JsonSerializerOptions options)
            => writer.WritePropertyName(value.ToString());
    }
}
```

See more examples in https://github.com/egil/framework/tree/main/Egil.StronglyTypedPrimitives/test/Egil.StronglyTypedPrimitives.Tests/snapshots

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

[System.CodeDom.Compiler.GeneratedCodeAttribute("Egil.StronglyTypedPrimitives, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", "1.0.0.0")]
[System.Text.Json.Serialization.JsonConverterAttribute(typeof(StronglyTypedIntWithConstraintsJsonConverter))]
public readonly partial record struct StronglyTypedIntWithConstraints : Egil.StronglyTypedPrimitives.IStronglyTypedPrimitive<int>, System.IParsable<Examples.StronglyTypedIntWithConstraints>, System.ISpanParsable<Examples.StronglyTypedIntWithConstraints>, System.IUtf8SpanParsable<Examples.StronglyTypedIntWithConstraints>, System.IComparable<Examples.StronglyTypedIntWithConstraints>, System.IComparable, System.IFormattable, System.ISpanFormattable, System.IUtf8SpanFormattable
{
    public static readonly StronglyTypedIntWithConstraints Empty = default;

    private static int ThrowIfValueIsInvalid(int value)
    {
        IsValueValid(value, throwIfInvalid: true);
        return value;
    }

    private readonly string @value = ThrowIfValueIsInvalid(Value);       

    public string Value
    {
        get => @value ?? string.Empty;
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

[System.CodeDom.Compiler.GeneratedCodeAttribute("Egil.StronglyTypedPrimitives, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", "1.0.0.0")]
[System.Text.Json.Serialization.JsonConverterAttribute(typeof(StronglyTypedIntWithConstraintsJsonConverter))]
public readonly partial record struct StronglyTypedIntWithConstraints : Egil.StronglyTypedPrimitives.IStronglyTypedPrimitive<int>, System.IParsable<Examples.StronglyTypedIntWithConstraints>, System.ISpanParsable<Examples.StronglyTypedIntWithConstraints>, System.IUtf8SpanParsable<Examples.StronglyTypedIntWithConstraints>, System.IComparable<Examples.StronglyTypedIntWithConstraints>, System.IComparable, System.IFormattable, System.ISpanFormattable, System.IUtf8SpanFormattable
{
    public static readonly StronglyTypedIntWithConstraints Empty = default;

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