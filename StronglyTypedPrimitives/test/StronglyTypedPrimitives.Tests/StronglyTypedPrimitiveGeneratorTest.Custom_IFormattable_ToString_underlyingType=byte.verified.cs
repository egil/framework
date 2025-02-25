﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
#nullable enable

namespace SomeNamespace;

[global::System.CodeDom.Compiler.GeneratedCodeAttribute("StronglyTypedPrimitives, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", "1.0.0.0")]
public readonly partial record struct Foo : global::StronglyTypedPrimitives.IStronglyTypedPrimitive<byte>, global::System.IParsable<global::SomeNamespace.Foo>, global::System.ISpanParsable<global::SomeNamespace.Foo>, global::System.IUtf8SpanParsable<global::SomeNamespace.Foo>, global::System.IComparable<global::SomeNamespace.Foo>, global::System.IComparable, global::System.IFormattable, global::System.ISpanFormattable, global::System.IUtf8SpanFormattable
{
    public static readonly Foo Empty = new Foo(default);

    private readonly byte @value = ThrowIfValueIsInvalid(Value);       

    public byte Value
    {
        get => @value;
        init
        {
            @value = ThrowIfValueIsInvalid(value);
        }
    }

    private static byte ThrowIfValueIsInvalid(byte value)
    {
        IsValueValid(value, throwIfInvalid: true);
        return value;
    }

    public static bool IsValueValid(byte value, bool throwIfInvalid)
        => true;

    public static Foo Parse(string s, global::System.IFormatProvider? provider)
    {
        var rawValue = byte.Parse(s, provider);
        IsValueValid(rawValue, throwIfInvalid: true);
        return new Foo(rawValue);
    }

    public static bool TryParse(string? s, global::System.IFormatProvider? provider, [global::System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(returnValue: false)] out SomeNamespace.Foo result)
    {
        if (byte.TryParse(s, provider, out var rawValue) && IsValueValid(rawValue, throwIfInvalid: false))
        {
            result = new Foo(rawValue);
            return true;
        }

        result = Foo.Empty;
        return false;
    }

    public static Foo Parse(global::System.ReadOnlySpan<char> s, global::System.IFormatProvider? provider)
    {
        var rawValue = byte.Parse(s, provider);
        IsValueValid(rawValue, throwIfInvalid: true);
        return new Foo(rawValue);
    }

    public static bool TryParse(global::System.ReadOnlySpan<char> s, global::System.IFormatProvider? provider, [global::System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(returnValue: false)] out SomeNamespace.Foo result)
    {
        if (byte.TryParse(s, provider, out var rawValue) && IsValueValid(rawValue, throwIfInvalid: false))
        {
            result = new Foo(rawValue);
            return true;
        }

        result = Foo.Empty;
        return false;
    }

    public static Foo Parse(global::System.ReadOnlySpan<byte> utf8Text, global::System.IFormatProvider? provider)
    {
        var rawValue = byte.Parse(utf8Text, provider);
        IsValueValid(rawValue, throwIfInvalid: true);
        return new Foo(rawValue);
    }
    
    public static bool TryParse(global::System.ReadOnlySpan<byte> utf8Text, global::System.IFormatProvider? provider, [global::System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(returnValue: false)] out SomeNamespace.Foo result)
    {
        if (byte.TryParse(utf8Text, provider, out var rawValue) && IsValueValid(rawValue, throwIfInvalid: false))
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

        return ((global::System.IComparable)Value).CompareTo(obj);
    }
    
    public string ToString(string? format, global::System.IFormatProvider? formatProvider)
        => Value.ToString(format, formatProvider);
    
    public bool TryFormat(global::System.Span<char> destination, out int charsWritten, global::System.ReadOnlySpan<char> format, global::System.IFormatProvider? provider)
        => ((global::System.ISpanFormattable)Value).TryFormat(destination, out charsWritten, format, provider);
    
    public bool TryFormat(global::System.Span<byte> utf8Destination, out int bytesWritten, global::System.ReadOnlySpan<char> format, global::System.IFormatProvider? provider)
        => ((global::System.IUtf8SpanFormattable)Value).TryFormat(utf8Destination, out bytesWritten, format, provider);
    
    public static bool operator > (Foo a, Foo b) => a.CompareTo(b) > 0;                

    public static bool operator < (Foo a, Foo b) => a.CompareTo(b) < 0;                

    public static bool operator >=(Foo a, Foo b) => a.CompareTo(b) >= 0;
    
    public static bool operator <=(Foo a, Foo b) => a.CompareTo(b) <= 0;
}