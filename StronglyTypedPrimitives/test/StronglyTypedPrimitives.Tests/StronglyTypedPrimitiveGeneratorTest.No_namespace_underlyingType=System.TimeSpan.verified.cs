﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
#nullable enable
[global::System.CodeDom.Compiler.GeneratedCodeAttribute("StronglyTypedPrimitives, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null", "0.0.0.0")]
public readonly partial record struct Foo : StronglyTypedPrimitives.IStronglyTypedPrimitive<System.TimeSpan>, System.IParsable<Foo>, System.ISpanParsable<Foo>, System.IComparable<Foo>, System.IComparable, System.IFormattable, System.ISpanFormattable, System.IUtf8SpanFormattable
{
    public static readonly Foo Empty = new Foo(default);

    private readonly System.TimeSpan @value = ThrowIfValueIsInvalid(Value);       

    public System.TimeSpan Value
    {
        get => @value;
        init
        {
            @value = ThrowIfValueIsInvalid(value);
        }
    }

    private static System.TimeSpan ThrowIfValueIsInvalid(System.TimeSpan value)
    {
        IsValueValid(value, throwIfInvalid: true);
        return value;
    }

    public override string ToString() => Value.ToString();

    public static bool IsValueValid(System.TimeSpan value, bool throwIfInvalid)
        => true;

    public static Foo Parse(string s, System.IFormatProvider? provider)
    {
        var rawValue = System.TimeSpan.Parse(s, provider);
        IsValueValid(rawValue, throwIfInvalid: true);
        return new Foo(rawValue);
    }

    public static bool TryParse(string? s, System.IFormatProvider? provider, [System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(returnValue: false)] out Foo result)
    {
        if (System.TimeSpan.TryParse(s, provider, out var rawValue) && IsValueValid(rawValue, throwIfInvalid: false))
        {
            result = new Foo(rawValue);
            return true;
        }

        result = Foo.Empty;
        return false;
    }

    public static Foo Parse(System.ReadOnlySpan<char> s, System.IFormatProvider? provider)
    {
        var rawValue = System.TimeSpan.Parse(s, provider);
        IsValueValid(rawValue, throwIfInvalid: true);
        return new Foo(rawValue);
    }

    public static bool TryParse(System.ReadOnlySpan<char> s, System.IFormatProvider? provider, [System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(returnValue: false)] out Foo result)
    {
        if (System.TimeSpan.TryParse(s, provider, out var rawValue) && IsValueValid(rawValue, throwIfInvalid: false))
        {
            result = new Foo(rawValue);
            return true;
        }

        result = Foo.Empty;
        return false;
    }
    
    public int CompareTo(Foo other)
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
}