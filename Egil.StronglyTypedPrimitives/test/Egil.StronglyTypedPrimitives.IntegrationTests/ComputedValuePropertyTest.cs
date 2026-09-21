using System.Text.Json;

namespace Egil.StronglyTypedPrimitives
{
    using Examples;

    public class ComputedValuePropertyTest
    {
        [Fact]
        public void Json_uses_the_positional_property_not_a_computed_Value_property()
        {
            var temperature = new Temperature(10);

            Assert.Equal(50, temperature.Value);

            var json = JsonSerializer.Serialize(temperature);

            Assert.Equal("10", json);
            Assert.Equal(temperature, JsonSerializer.Deserialize<Temperature>(json));
        }
    }
}