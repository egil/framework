
namespace StronglyTypedPrimitives;

public class ExampleTest
{
    [Fact]
    public void Ensure_generated_type_is_either_None_or_pass_IsValueValid()
    {
        Assert.Equal(Foo.None, default(Foo));
        Assert.Equal(Foo.None, new Foo());
        Assert.NotEqual(Foo.None, new Foo(6));
        Assert.Throws<ArgumentException>(() => new Foo(1));
        Assert.Throws<ArgumentException>(() => Foo.None with { Value = 1 });
    }

    private readonly partial record struct Foo(int Value);

    [System.CodeDom.Compiler.GeneratedCodeAttribute("StronglyTypedPrimitives, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", "1.0.0.0")]
    private readonly partial record struct Foo : IParsable<Foo>, ISpanParsable<Foo>, IFormattable, ISpanFormattable
    {
        public static readonly Foo None = new Foo();

        private readonly int @value = ThrowIfValueIsInvalid(Value);

        public int Value
        {
            get => @value;
            init
            {
                @value = ThrowIfValueIsInvalid(value);
            }
        }

        private static int ThrowIfValueIsInvalid(int value)
        {
            IsValueValid(value, throwIfInvalid: true);
            return value;
        }

        [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static bool IsValueValid(int value, bool throwIfInvalid)
        {
            if (value > 5)
            {
                return true;
            }

            if (throwIfInvalid)
            {
                throw new ArgumentException("Value is invalid");
            }

            return false;
        }

        public static Foo Parse(string? s) => Parse(s, provider: null);

        public static Foo Parse(string? s, global::System.IFormatProvider? provider)
        {
            var underlying = int.Parse(s!, provider);
            IsValueValid(underlying, throwIfInvalid: true);
            return new Foo(underlying);
        }

        public static bool TryParse(string? s, out Foo result) => TryParse(s, provider: null, out result);

        public static bool TryParse(string? s, global::System.IFormatProvider? provider, out Foo result)
        {
            if (int.TryParse(s, provider, out var underlying) && IsValueValid(underlying, throwIfInvalid: false))
            {
                result = new Foo(underlying);
                return true;
            }

            result = default;
            return false;
        }

        public string ToString(string? format, IFormatProvider? formatProvider)
            => Value.ToString(format, formatProvider);

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
            => Value.TryFormat(destination, out charsWritten, format, provider);

        public static Foo Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
            => throw new NotImplementedException();

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out Foo result)
            => throw new NotImplementedException();
    }
}
