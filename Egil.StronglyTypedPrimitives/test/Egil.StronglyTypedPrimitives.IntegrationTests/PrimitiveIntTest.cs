using System.Globalization;
using System.Text.Json;
using Egil.StronglyTypedPrimitives;

namespace Egil.StronglyTypedPrimitives
{
    using Examples;

    public partial class PrimitiveIntTest
    {
        [Fact]
        public void StronglyTyped_string_methods()
        {
            var tooShortString = "12345";
            var tooShortSpan = tooShortString.AsSpan();
            var goodString = "123456";
            var goodSpan = goodString.AsSpan();

            Assert.Equal(StronglyTypedString.Empty, default(StronglyTypedString));
            Assert.NotEqual(StronglyTypedString.Empty, new StronglyTypedString(goodString));
            Assert.Equal(string.Empty, StronglyTypedString.Empty.ToString());

            Assert.Throws<ArgumentException>(() => new StronglyTypedString(tooShortString));
            Assert.Throws<ArgumentException>(() => StronglyTypedString.Empty with { Value = tooShortString });

            Assert.Throws<ArgumentException>(() => StronglyTypedString.Parse(tooShortString, null));
            Assert.Equal(new StronglyTypedString(goodString), StronglyTypedString.Parse(goodString, null));

            Assert.False(StronglyTypedString.TryParse(tooShortString, null, out var _));
            Assert.True(StronglyTypedString.TryParse(goodString, null, out var tryParsedString));
            Assert.Equal(new StronglyTypedString(goodString), tryParsedString);

            Assert.Throws<ArgumentException>(() => StronglyTypedString.Parse(tooShortString, null));
            Assert.Equal(new StronglyTypedString(goodString), StronglyTypedString.Parse(goodString, null));

            Assert.False(StronglyTypedString.TryParse(tooShortSpan, null, out var _));
            Assert.True(StronglyTypedString.TryParse(goodSpan, null, out var tryParsedSpan));
            Assert.Equal(new StronglyTypedString(goodString), tryParsedSpan);

            Assert.Equal(goodString, new StronglyTypedString(goodString).ToString());
            Assert.Equal("bbbbbb".CompareTo("cbbbbb"), new StronglyTypedString("bbbbbb").CompareTo(new StronglyTypedString("cbbbbb")));
            Assert.True(new StronglyTypedString("bbbbbb") < new StronglyTypedString("cbbbbb"));
            Assert.False(new StronglyTypedString("bbbbbb") > new StronglyTypedString("cbbbbb"));
            Assert.True(new StronglyTypedString("cbbbbb") <= new StronglyTypedString("cbbbbb"));
            Assert.True(new StronglyTypedString("cbbbbb") >= new StronglyTypedString("cbbbbb"));
            Assert.Equal("bbbbbb".CompareTo("cbbbbb"), new StronglyTypedString("bbbbbb").CompareTo("cbbbbb"));
            Assert.Throws<ArgumentException>(() => new StronglyTypedString("bbbbbb").CompareTo(42));

            Assert.Equal(new StronglyTypedString(goodString), JsonSerializer.Deserialize<StronglyTypedString>(JsonSerializer.Serialize(new StronglyTypedString(goodString))));
            Assert.Equal(StronglyTypedString.Empty, JsonSerializer.Deserialize<StronglyTypedString>($"\"{tooShortString}\""));
            Assert.Equal($"\"{goodString}\"", JsonSerializer.Serialize(new StronglyTypedString(goodString)));
            Assert.Equal(new StronglyTypedString(goodString), JsonSerializer.Deserialize<StronglyTypedString>($"\"{goodString}\""));
        }

        [Fact]
        [SuppressMessage("Globalization", "CA1305:Specify IFormatProvider", Justification = "Ignore")]
        public void StronglyTyped_int_methods()
        {
            var tooLowString = "5";
            var tooLowValue = 5;
            var tooLowSpan = tooLowString.AsSpan();
            var goodString = "6";
            var goodValue = 6;
            var goodSpan = goodString.AsSpan();

            Assert.Equal(StronglyTypedIntWithConstraints.Empty, default(StronglyTypedIntWithConstraints));
            Assert.NotEqual(StronglyTypedIntWithConstraints.Empty, new StronglyTypedIntWithConstraints(goodValue));
            Assert.Equal(default(int).ToString(), StronglyTypedIntWithConstraints.Empty.ToString());

            Assert.Throws<ArgumentException>(() => new StronglyTypedIntWithConstraints(tooLowValue));
            Assert.Throws<ArgumentException>(() => StronglyTypedIntWithConstraints.Empty with { Value = tooLowValue });

            Assert.Throws<ArgumentException>(() => StronglyTypedIntWithConstraints.Parse(tooLowString, CultureInfo.InvariantCulture));
            Assert.Equal(new StronglyTypedIntWithConstraints(goodValue), StronglyTypedIntWithConstraints.Parse(goodString, CultureInfo.InvariantCulture));

            Assert.False(StronglyTypedIntWithConstraints.TryParse(tooLowString, null, out var _));
            Assert.True(StronglyTypedIntWithConstraints.TryParse(goodString, null, out var tryParsedString));
            Assert.Equal(new StronglyTypedIntWithConstraints(goodValue), (StronglyTypedIntWithConstraints)tryParsedString);

            Assert.Throws<ArgumentException>(() => StronglyTypedIntWithConstraints.Parse(tooLowString, CultureInfo.InvariantCulture));
            Assert.Equal(new StronglyTypedIntWithConstraints(goodValue), StronglyTypedIntWithConstraints.Parse(goodString, CultureInfo.InvariantCulture));

            Assert.False(StronglyTypedIntWithConstraints.TryParse(tooLowSpan, CultureInfo.InvariantCulture, out var _));
            Assert.True(StronglyTypedIntWithConstraints.TryParse(goodSpan, CultureInfo.InvariantCulture, out var tryParsedSpan));
            Assert.Equal(new StronglyTypedIntWithConstraints(goodValue), (StronglyTypedIntWithConstraints)tryParsedSpan);

            Assert.Equal(goodValue.ToString(), new StronglyTypedIntWithConstraints(goodValue).ToString());
            Assert.Equal(6.CompareTo(7), new StronglyTypedIntWithConstraints(6).CompareTo(new StronglyTypedIntWithConstraints(77)));
            Assert.True((bool)(new StronglyTypedIntWithConstraints(6) < new StronglyTypedIntWithConstraints(7)));
            Assert.False((bool)(new StronglyTypedIntWithConstraints(6) > new StronglyTypedIntWithConstraints(7)));
            Assert.True((bool)(new StronglyTypedIntWithConstraints(7) <= new StronglyTypedIntWithConstraints(7)));
            Assert.True((bool)(new StronglyTypedIntWithConstraints(7) >= new StronglyTypedIntWithConstraints(7)));
            Assert.Equal(6.CompareTo(7), new StronglyTypedIntWithConstraints(6).CompareTo(7));
            Assert.Throws<ArgumentException>(() => new StronglyTypedIntWithConstraints(6).CompareTo("asdf"));

            Assert.Equal(new StronglyTypedIntWithConstraints(goodValue), JsonSerializer.Deserialize<StronglyTypedIntWithConstraints>(JsonSerializer.Serialize(new StronglyTypedIntWithConstraints(goodValue))));
            Assert.Equal(StronglyTypedIntWithConstraints.Empty, JsonSerializer.Deserialize<StronglyTypedIntWithConstraints>(tooLowString.ToString()));
            Assert.Equal(goodValue.ToString(), JsonSerializer.Serialize(new StronglyTypedIntWithConstraints(goodValue)));
            Assert.Equal(new StronglyTypedIntWithConstraints(goodValue), JsonSerializer.Deserialize<StronglyTypedIntWithConstraints>(goodValue.ToString()));
        }

        [Fact]
        [SuppressMessage("Globalization", "CA1305:Specify IFormatProvider", Justification = "Ignore")]
        public void StronglyTyped_int_no_constraints()
        {
            var goodString = "6";
            var goodValue = 6;
            var goodSpan = goodString.AsSpan();

            Assert.Equal(StronglyTypedInt.Empty, default(StronglyTypedInt));
            Assert.NotEqual(StronglyTypedInt.Empty, new StronglyTypedInt(goodValue));
            Assert.Equal(default(int).ToString(), StronglyTypedInt.Empty.ToString());

            Assert.Equal(new StronglyTypedInt(goodValue), StronglyTypedInt.Parse(goodString, CultureInfo.InvariantCulture));

            Assert.True(StronglyTypedInt.TryParse(goodString, null, out var tryParsedString));
            Assert.Equal(new StronglyTypedInt(goodValue), (StronglyTypedInt)tryParsedString);

            Assert.Equal(new StronglyTypedInt(goodValue), StronglyTypedInt.Parse(goodString, CultureInfo.InvariantCulture));

            Assert.True(StronglyTypedInt.TryParse(goodSpan, CultureInfo.InvariantCulture, out var tryParsedSpan));
            Assert.Equal(new StronglyTypedInt(goodValue), (StronglyTypedInt)tryParsedSpan);

            Assert.Equal(goodValue.ToString(), new StronglyTypedInt(goodValue).ToString());
            Assert.Equal(6.CompareTo(7), new StronglyTypedInt(6).CompareTo(new StronglyTypedInt(77)));
            Assert.True((bool)(new StronglyTypedInt(6) < new StronglyTypedInt(7)));
            Assert.False((bool)(new StronglyTypedInt(6) > new StronglyTypedInt(7)));
            Assert.True((bool)(new StronglyTypedInt(7) <= new StronglyTypedInt(7)));
            Assert.True((bool)(new StronglyTypedInt(7) >= new StronglyTypedInt(7)));
            Assert.Equal(6.CompareTo(7), new StronglyTypedInt(6).CompareTo(7));
            Assert.Throws<ArgumentException>(() => new StronglyTypedInt(6).CompareTo("asdf"));

            Assert.Equal(new StronglyTypedInt(goodValue), JsonSerializer.Deserialize<StronglyTypedInt>(JsonSerializer.Serialize(new StronglyTypedInt(goodValue))));
            Assert.Equal(goodValue.ToString(), JsonSerializer.Serialize(new StronglyTypedInt(goodValue)));
            Assert.Equal(new StronglyTypedInt(goodValue), JsonSerializer.Deserialize<StronglyTypedInt>(goodValue.ToString()));
        }
    }
}

namespace Examples
{
    [StronglyTyped]
    public readonly partial record struct StronglyTypedString(string Value)
    {
        public static bool IsValueValid(string value, bool throwIfInvalid)
        {
            if (value is { Length: > 5 })
                return true;

            if (throwIfInvalid)
                throw new ArgumentException("Value must be at least 6 characters long", nameof(value));

            return false;
        }
    }

    [StronglyTyped]
    public readonly partial record struct StronglyTypedIntWithConstraints(int Value)
    {
        public static bool IsValueValid(int value, bool throwIfInvalid)
        {
            if (value > 5)
                return true;

            if (throwIfInvalid)
                throw new ArgumentException("Value must be at larger than 5", nameof(value));

            return false;
        }
    }

    [StronglyTyped]
    public readonly partial record struct StronglyTypedInt(int Value);
}