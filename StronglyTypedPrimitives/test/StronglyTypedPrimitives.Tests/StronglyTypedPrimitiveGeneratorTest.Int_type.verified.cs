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
public readonly partial record struct Foo :
    global::StronglyTypedPrimitives.IStronglyTypedPrimitive
{
    public static readonly Foo None = new Foo(default);
    
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static bool IsValid(int value) => true;            

    public int Value { get; init; } = ThrowIfInvalid(Value);

    private static int ThrowIfInvalid(int value)
    {
        if (!IsValid(value))
        {
            throw new global::System.ArgumentException($"The value '{value}' is not valid for Foo.", "Value");
        }

        return value;
    }
}