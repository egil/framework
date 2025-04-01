﻿//HintName: Foo.g.cs
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
#nullable enable

namespace SomeNamespace;

[System.CodeDom.Compiler.GeneratedCodeAttribute("Egil.StronglyTypedPrimitives, Version=x.x.x.x, Culture=neutral, PublicKeyToken=null", "x.x.x.x")]
[System.Text.Json.Serialization.JsonConverterAttribute(typeof(FooJsonConverter))]
public readonly partial record struct Foo : Egil.StronglyTypedPrimitives.IStronglyTypedPrimitive<decimal>, System.IParsable<SomeNamespace.Foo>, System.ISpanParsable<SomeNamespace.Foo>, System.IUtf8SpanParsable<SomeNamespace.Foo>, System.IComparable<SomeNamespace.Foo>, System.IComparable, System.IFormattable, System.ISpanFormattable, System.IUtf8SpanFormattable
{
    public static readonly Foo Empty = default;

    private static decimal ThrowIfValueIsInvalid(decimal value)
    {
        IsValueValid(value, throwIfInvalid: true);
        return value;
    }

    private readonly decimal @value = ThrowIfValueIsInvalid(Value);

    public decimal Value
    {
        get => @value;
        init
        {
            @value = ThrowIfValueIsInvalid(value);
        }
    }

    public override string ToString() => Value.ToString();

    public static Foo Parse(string s, System.IFormatProvider? provider)
    {
        var rawValue = decimal.Parse(s, provider);
        IsValueValid(rawValue, throwIfInvalid: true);
        return new Foo(rawValue);
    }

    public static bool TryParse(string? s, System.IFormatProvider? provider, [System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(returnValue: false)] out SomeNamespace.Foo result)
    {
        if (decimal.TryParse(s, provider, out var rawValue) && IsValueValid(rawValue, throwIfInvalid: false))
        {
            result = new Foo(rawValue);
            return true;
        }

        result = Foo.Empty;
        return false;
    }

    public static Foo Parse(System.ReadOnlySpan<char> s, System.IFormatProvider? provider)
    {
        var rawValue = decimal.Parse(s, provider);
        IsValueValid(rawValue, throwIfInvalid: true);
        return new Foo(rawValue);
    }

    public static bool TryParse(System.ReadOnlySpan<char> s, System.IFormatProvider? provider, [System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(returnValue: false)] out SomeNamespace.Foo result)
    {
        if (decimal.TryParse(s, provider, out var rawValue) && IsValueValid(rawValue, throwIfInvalid: false))
        {
            result = new Foo(rawValue);
            return true;
        }

        result = Foo.Empty;
        return false;
    }

    public static Foo Parse(System.ReadOnlySpan<byte> utf8Text, System.IFormatProvider? provider)
    {
        var rawValue = decimal.Parse(utf8Text, provider);
        IsValueValid(rawValue, throwIfInvalid: true);
        return new Foo(rawValue);
    }

    public static bool TryParse(System.ReadOnlySpan<byte> utf8Text, System.IFormatProvider? provider, [System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(returnValue: false)] out SomeNamespace.Foo result)
    {
        if (decimal.TryParse(utf8Text, provider, out var rawValue) && IsValueValid(rawValue, throwIfInvalid: false))
        {
            result = new Foo(rawValue);
            return true;
        }

        result = Foo.Empty;
        return false;
    }
    
    public int CompareTo(SomeNamespace.Foo other)
        => Value.CompareTo(other.Value);
    
    public int CompareTo(object? obj)
    {
        if (obj is null)
        {
            return 1;
        }

        if (obj is Foo other)
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
    
    public static bool operator > (Foo a, Foo b) => a.CompareTo(b) > 0;                

    public static bool operator < (Foo a, Foo b) => a.CompareTo(b) < 0;                

    public static bool operator >=(Foo a, Foo b) => a.CompareTo(b) >= 0;
    
    public static bool operator <=(Foo a, Foo b) => a.CompareTo(b) <= 0;

    public sealed class FooJsonConverter : System.Text.Json.Serialization.JsonConverter<Foo>
    {
        public override Foo Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        {
            var rawValue = System.Text.Json.JsonSerializer.Deserialize<decimal>(ref reader, options);
            
            return Foo.IsValueValid(rawValue, throwIfInvalid: false)
                ? new Foo(rawValue)
                : Foo.Empty;
        }

        public override void Write(System.Text.Json.Utf8JsonWriter writer, Foo value, System.Text.Json.JsonSerializerOptions options)
            => System.Text.Json.JsonSerializer.Serialize(writer, value.Value, options);
    }
}