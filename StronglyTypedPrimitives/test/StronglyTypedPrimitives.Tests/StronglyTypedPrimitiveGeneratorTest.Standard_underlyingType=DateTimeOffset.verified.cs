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
public readonly partial record struct Foo : global::StronglyTypedPrimitives.IStronglyTypedPrimitive
{
    public static readonly Foo None = new Foo(default);

    private readonly DateTimeOffset @value = ThrowIfValueIsInvalid(Value);       

    public DateTimeOffset Value
    {
        get => @value;
        init
        {
            @value = ThrowIfValueIsInvalid(value);
        }
    }

    private static DateTimeOffset ThrowIfValueIsInvalid(DateTimeOffset value)
    {
        IsValueValid(value, throwIfInvalid: true);
        return value;
    }

    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static bool IsValueValid(DateTimeOffset value, bool throwIfInvalid) => true;

    public static Foo Parse(string? s, global::System.IFormatProvider? provider)
    {
        var rawValue = DateTimeOffset.Parse(s!, provider);
        IsValueValid(rawValue, throwIfInvalid: true);
        return new Foo(rawValue);
    }

    public static bool TryParse(string? s, global::System.IFormatProvider? provider, out Foo result)
    {
        if (DateTimeOffset.TryParse(s, provider, out var rawValue) && IsValueValid(rawValue, throwIfInvalid: false))
        {
            result = new Foo(rawValue);
            return true;
        }

        result = default;
        return false;
    }
}