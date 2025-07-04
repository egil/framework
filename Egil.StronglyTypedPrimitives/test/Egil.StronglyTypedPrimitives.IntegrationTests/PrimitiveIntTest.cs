using System.Globalization;
using System.Text.Json;

namespace Egil.StronglyTypedPrimitives
{
    using Examples;

    public partial class PrimitiveIntTest
    {
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
            Assert.Equal(new StronglyTypedIntWithConstraints(goodValue), tryParsedString);

            Assert.Throws<ArgumentException>(() => StronglyTypedIntWithConstraints.Parse(tooLowString, CultureInfo.InvariantCulture));
            Assert.Equal(new StronglyTypedIntWithConstraints(goodValue), StronglyTypedIntWithConstraints.Parse(goodString, CultureInfo.InvariantCulture));

            Assert.False(StronglyTypedIntWithConstraints.TryParse(tooLowSpan, CultureInfo.InvariantCulture, out var _));
            Assert.True(StronglyTypedIntWithConstraints.TryParse(goodSpan, CultureInfo.InvariantCulture, out var tryParsedSpan));
            Assert.Equal(new StronglyTypedIntWithConstraints(goodValue), tryParsedSpan);

            Assert.Equal(goodValue.ToString(), new StronglyTypedIntWithConstraints(goodValue).ToString());
            Assert.Equal(6.CompareTo(7), new StronglyTypedIntWithConstraints(6).CompareTo(new StronglyTypedIntWithConstraints(7)));
            Assert.True(new StronglyTypedIntWithConstraints(6) < new StronglyTypedIntWithConstraints(7));
            Assert.False(new StronglyTypedIntWithConstraints(6) > new StronglyTypedIntWithConstraints(7));
            Assert.True(new StronglyTypedIntWithConstraints(7) <= new StronglyTypedIntWithConstraints(7));
            Assert.True(new StronglyTypedIntWithConstraints(7) >= new StronglyTypedIntWithConstraints(7));
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
            Assert.Equal(new StronglyTypedInt(goodValue), tryParsedString);

            Assert.Equal(new StronglyTypedInt(goodValue), StronglyTypedInt.Parse(goodString, CultureInfo.InvariantCulture));

            Assert.True(StronglyTypedInt.TryParse(goodSpan, CultureInfo.InvariantCulture, out var tryParsedSpan));
            Assert.Equal(new StronglyTypedInt(goodValue), tryParsedSpan);

            Assert.Equal(goodValue.ToString(), new StronglyTypedInt(goodValue).ToString());
            Assert.Equal(6.CompareTo(7), new StronglyTypedInt(6).CompareTo(new StronglyTypedInt(7)));
            Assert.True(new StronglyTypedInt(6) < new StronglyTypedInt(7));
            Assert.False(new StronglyTypedInt(6) > new StronglyTypedInt(7));
            Assert.True(new StronglyTypedInt(7) <= new StronglyTypedInt(7));
            Assert.True(new StronglyTypedInt(7) >= new StronglyTypedInt(7));
            Assert.Equal(6.CompareTo(7), new StronglyTypedInt(6).CompareTo(7));
            Assert.Throws<ArgumentException>(() => new StronglyTypedInt(6).CompareTo("asdf"));

            Assert.Equal(new StronglyTypedInt(goodValue), JsonSerializer.Deserialize<StronglyTypedInt>(JsonSerializer.Serialize(new StronglyTypedInt(goodValue))));
            Assert.Equal(goodValue.ToString(), JsonSerializer.Serialize(new StronglyTypedInt(goodValue)));
            Assert.Equal(new StronglyTypedInt(goodValue), JsonSerializer.Deserialize<StronglyTypedInt>(goodValue.ToString()));
        }
    }
}