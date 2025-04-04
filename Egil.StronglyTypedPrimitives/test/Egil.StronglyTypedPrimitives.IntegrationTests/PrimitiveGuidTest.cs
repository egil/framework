using Egil.StronglyTypedPrimitives;
using System.Text.Json;

namespace Egil.StronglyTypedPrimitives
{
    using Examples;
    using System;
    using Xunit;

    public partial class PrimitiveGuidTest
    {
        [Fact]
        public void StronglyTyped_guid_methods()
        {
            var goodGuid = Guid.NewGuid();
            var goodGuidString = goodGuid.ToString();
            var goodSpan = goodGuidString.ToString().AsSpan();

            Assert.Equal(StronglyTypedGuid.Empty, default(StronglyTypedGuid));
            Assert.NotEqual(StronglyTypedGuid.Empty, new StronglyTypedGuid(goodGuid));
            Assert.Equal(Guid.Empty, StronglyTypedGuid.Empty.Value);

            Assert.Equal(new StronglyTypedGuid(goodGuid), StronglyTypedGuid.Parse(goodGuidString, null));

            Assert.True(StronglyTypedGuid.TryParse(goodGuidString, null, out var tryParsedString));
            Assert.Equal(new StronglyTypedGuid(goodGuid), tryParsedString);

            Assert.Equal(new StronglyTypedGuid(goodGuid), StronglyTypedGuid.Parse(goodGuidString, null));

            Assert.True(StronglyTypedGuid.TryParse(goodSpan, null, out var tryParsedSpan));
            Assert.Equal(new StronglyTypedGuid(goodGuid), tryParsedSpan);

            Assert.Equal(goodGuidString, new StronglyTypedGuid(goodGuid).ToString());
            Assert.True(new StronglyTypedGuid(goodGuid) == new StronglyTypedGuid(goodGuid));
            Assert.False(new StronglyTypedGuid(goodGuid) != new StronglyTypedGuid(goodGuid));

            Assert.Equal(new StronglyTypedGuid(goodGuid), JsonSerializer.Deserialize<StronglyTypedGuid>(JsonSerializer.Serialize(new StronglyTypedGuid(goodGuid))));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StronglyTypedGuid>("null"));
            Assert.Equal($"\"{goodGuidString}\"", JsonSerializer.Serialize(new StronglyTypedGuid(goodGuid)));
            Assert.Equal(new StronglyTypedGuid(goodGuid), JsonSerializer.Deserialize<StronglyTypedGuid>($"\"{goodGuidString}\""));

            Assert.False(new StronglyTypedGuid(goodGuid) == default);
            Assert.True(new StronglyTypedGuid(goodGuid) != default);
            Assert.False(new StronglyTypedGuid(goodGuid) == StronglyTypedGuid.Empty);
            Assert.True(new StronglyTypedGuid(goodGuid) != StronglyTypedGuid.Empty);
            Assert.False(new StronglyTypedGuid(goodGuid) < StronglyTypedGuid.Empty);
            Assert.True(new StronglyTypedGuid(goodGuid) > StronglyTypedGuid.Empty);
            Assert.False(new StronglyTypedGuid(goodGuid) <= StronglyTypedGuid.Empty);
            Assert.True(new StronglyTypedGuid(goodGuid) >= StronglyTypedGuid.Empty);
        }
    }
}

namespace Examples
{
    [StronglyTyped]
    public readonly partial record struct StronglyTypedGuid(Guid Value);
}