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
public readonly partial record struct Foo : global::StronglyTypedPrimitives.IStronglyTypedPrimitive, global::System.IParsable<Foo>
{
    public static readonly Foo Empty = new Foo(default);

    private readonly System.DateTime @value = ThrowIfValueIsInvalid(Value);       

    public System.DateTime Value
    {
        get => @value;
        init
        {
            @value = ThrowIfValueIsInvalid(value);
        }
    }

    private static System.DateTime ThrowIfValueIsInvalid(System.DateTime value)
    {
        IsValueValid(value, throwIfInvalid: true);
        return value;
    }

    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static bool IsValueValid(System.DateTime value, bool throwIfInvalid) => true;

    public override string ToString() => Value.ToString();

    public static bool TryParse(string? s, global::System.IFormatProvider? provider, out Foo result)
    {
        if (System.DateTime.TryParse(s, provider, out var rawValue) && IsValueValid(rawValue, throwIfInvalid: false))
        {
            result = new Foo(rawValue);
            return true;
        }

        result = default;
        return false;
    }
}