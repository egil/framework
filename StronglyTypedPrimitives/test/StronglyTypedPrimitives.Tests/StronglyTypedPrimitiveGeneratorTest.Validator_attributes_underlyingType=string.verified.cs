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
    
    private static readonly global::System.ComponentModel.DataAnnotations.ValidationAttribute[] Validators =
    [
        new global::System.ComponentModel.DataAnnotations.RangeAttribute(50, 100),
        new global::System.ComponentModel.DataAnnotations.RequiredAttribute(),
        new global::System.ComponentModel.DataAnnotations.RegularExpressionAttribute("^[a-zA-Z''-'\\s]{1,40}$"),
        new global::System.ComponentModel.DataAnnotations.DeniedValuesAttribute(new [] {"foo", "bar"})
    ];

    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static bool IsValueValid(string value, bool throwIfInvalid)
    {
        for (var i = 0; i < Validators.Length; i++)
        {
            if (!Validators[i].IsValid(value))
            {
                return false;
            }
        }

        return true;
    }

    public override string ToString() => Value.ToString();

    public static Foo Parse(string? s, global::System.IFormatProvider? provider)
    {
        global::System.ArgumentNullException.ThrowIfNull(s);
        IsValueValid(s, throwIfInvalid: true);
        return new Foo(s);
    }

    public static bool TryParse(string? s, global::System.IFormatProvider? provider, out Foo result)
    {
        if (s is not null && IsValueValid(s, throwIfInvalid: false))
        {
            result = new Foo(s);
            return true;
        }

        result = default;
        return false;
    }
}