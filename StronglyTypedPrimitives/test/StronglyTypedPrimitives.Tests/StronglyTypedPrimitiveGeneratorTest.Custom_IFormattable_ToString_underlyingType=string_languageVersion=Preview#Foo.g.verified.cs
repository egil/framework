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

[System.CodeDom.Compiler.GeneratedCodeAttribute("StronglyTypedPrimitives, Version=x.x.x.x, Culture=neutral, PublicKeyToken=null", "x.x.x.x")]
public readonly partial record struct Foo : StronglyTypedPrimitives.IStronglyTypedPrimitive<string>, System.IParsable<SomeNamespace.Foo>, System.ISpanParsable<SomeNamespace.Foo>, System.IComparable<SomeNamespace.Foo>, System.IComparable
{
    public static readonly Foo Empty = default;

    private static string ThrowIfValueIsInvalid(string value)
    {
        IsValueValid(value, throwIfInvalid: true);
        return value;
    }

    public string Value
    {
        get => field ?? string.Empty;
        init
        {
            field = ThrowIfValueIsInvalid(value);
        }
    } = ThrowIfValueIsInvalid(Value);

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