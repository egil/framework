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

[global::System.CodeDom.Compiler.GeneratedCodeAttribute("StronglyTypedPrimitives, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null", "0.0.0.0")]
public readonly partial record struct Foo : StronglyTypedPrimitives.IStronglyTypedPrimitive<string>, System.IParsable<SomeNamespace.Foo>, System.ISpanParsable<SomeNamespace.Foo>, System.IComparable<SomeNamespace.Foo>, System.IComparable
{
    public static readonly Foo Empty = new Foo(string.Empty);

    private readonly string @value = ThrowIfValueIsInvalid(Value);       

    public string Value
    {
        get => @value;
        init
        {
            @value = ThrowIfValueIsInvalid(value);
        }
    }

    private static string ThrowIfValueIsInvalid(string value)
    {
        IsValueValid(value, throwIfInvalid: true);
        return value;
    }

    public override string ToString() => Value.ToString();

    public static bool IsValueValid(string value, bool throwIfInvalid)
        => true;

    public static Foo Parse(string s, System.IFormatProvider? provider)
    {
        var rawValue = s;
        IsValueValid(rawValue, throwIfInvalid: true);
        return new Foo(rawValue);
    }

    public static bool TryParse(string? s, System.IFormatProvider? provider, [System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(returnValue: false)] out SomeNamespace.Foo result)
    {
        if (s is {} rawValue && IsValueValid(rawValue, throwIfInvalid: false))
        {
            result = new Foo(rawValue);
            return true;
        }

        result = Foo.Empty;
        return false;
    }

    public static Foo Parse(System.ReadOnlySpan<char> s, System.IFormatProvider? provider)
    {
        var rawValue = s.ToString();
        IsValueValid(rawValue, throwIfInvalid: true);
        return new Foo(rawValue);
    }

    public static bool TryParse(System.ReadOnlySpan<char> s, System.IFormatProvider? provider, [System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(returnValue: false)] out SomeNamespace.Foo result)
    {
        if (s.ToString() is {} rawValue && IsValueValid(rawValue, throwIfInvalid: false))
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
    
    public static bool operator > (Foo a, Foo b) => a.CompareTo(b) > 0;                

    public static bool operator < (Foo a, Foo b) => a.CompareTo(b) < 0;                

    public static bool operator >=(Foo a, Foo b) => a.CompareTo(b) >= 0;
    
    public static bool operator <=(Foo a, Foo b) => a.CompareTo(b) <= 0;
}