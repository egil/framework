using System.Diagnostics;
using System.Runtime.CompilerServices;

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
        new Foo(1);
    }

    private readonly partial record struct Foo(int Value);

    [System.CodeDom.Compiler.GeneratedCodeAttribute("StronglyTypedPrimitives, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", "1.0.0.0")]
    private readonly partial record struct Foo
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
    }
}
