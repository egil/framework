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
public readonly partial record struct Foo : Egil.StronglyTypedPrimitives.IStronglyTypedPrimitive<string>, System.IParsable<SomeNamespace.Foo>, System.ISpanParsable<SomeNamespace.Foo>, System.IComparable<SomeNamespace.Foo>, System.IComparable
{
    public static readonly Foo Empty = default;

    public override string ToString() => Value;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static bool IsValueValid(string value, bool throwIfInvalid)
        => true;

    public static Foo Parse(string s, System.IFormatProvider? provider)
    {
        var rawValue = s;
        IsValueValid(rawValue, throwIfInvalid: true);
        return new Foo(rawValue);
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

    private sealed class FooJsonConverter : System.Text.Json.Serialization.JsonConverter<Foo>
    {
        public override Foo Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        {
            var rawValue = System.Text.Json.JsonSerializer.Deserialize<string>(ref reader, options);
            
            return rawValue is not null && Foo.IsValueValid(rawValue, throwIfInvalid: false)
                ? new Foo(rawValue)
                : Foo.Empty;
        }

        public override void Write(System.Text.Json.Utf8JsonWriter writer, Foo value, System.Text.Json.JsonSerializerOptions options)
            => System.Text.Json.JsonSerializer.Serialize(writer, value.Value, options);
    }
}