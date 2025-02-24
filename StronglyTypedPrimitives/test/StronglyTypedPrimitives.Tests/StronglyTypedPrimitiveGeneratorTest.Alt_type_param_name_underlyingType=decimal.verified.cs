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

[System.CodeDom.Compiler.GeneratedCodeAttribute("StronglyTypedPrimitives, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", "1.0.0.0")]
public readonly partial record struct Foo : global::StronglyTypedPrimitives.IStronglyTypedPrimitive, global::System.IParsable<Foo>, global::System.IFormattable
{
    public static readonly Foo Empty = new Foo(default);

    private readonly decimal @data = ThrowIfValueIsInvalid(Data);       

    public decimal Data
    {
        get => @data;
        init
        {
            @data = ThrowIfValueIsInvalid(value);
        }
    }

    private static decimal ThrowIfValueIsInvalid(decimal value)
    {
        IsValueValid(value, throwIfInvalid: true);
        return value;
    }

    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static bool IsValueValid(decimal value, bool throwIfInvalid) => true;

    public static Foo Parse(string? s, global::System.IFormatProvider? provider)
    {
        var rawValue = decimal.Parse(s!, provider);
        IsValueValid(rawValue, throwIfInvalid: true);
        return new Foo(rawValue);
    }

    public static bool TryParse(string? s, global::System.IFormatProvider? provider, out Foo result)
    {
        if (decimal.TryParse(s, provider, out var rawValue) && IsValueValid(rawValue, throwIfInvalid: false))
        {
            result = new Foo(rawValue);
            return true;
        }

        result = default;
        return false;
    }

    public override string ToString() => Data.ToString();

    public string ToString(string? format) => Data.ToString(format, null);

    public string ToString(string? format, global::System.IFormatProvider? formatProvider)
        => Data.ToString(format, formatProvider);
}