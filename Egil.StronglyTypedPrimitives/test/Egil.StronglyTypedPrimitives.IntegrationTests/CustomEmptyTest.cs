using System.Text.Json;

namespace Egil.StronglyTypedPrimitives
{
    using Examples;

    public class CustomEmptyTest
    {
        [Fact]
        public void User_declared_Empty_field_replaces_the_generated_one()
        {
            var tooLowString = "5";

            Assert.NotEqual(default, StronglyTypedIntWithEmptyField.Empty);

            Assert.False(StronglyTypedIntWithEmptyField.TryParse(tooLowString, null, out var parsed));
            Assert.Equal(StronglyTypedIntWithEmptyField.Empty, parsed);

            Assert.Equal(StronglyTypedIntWithEmptyField.Empty, JsonSerializer.Deserialize<StronglyTypedIntWithEmptyField>(tooLowString));
        }

        [Fact]
        public void User_declared_Empty_property_replaces_the_generated_one()
        {
            var tooLowString = "5";

            Assert.NotEqual(default, StronglyTypedIntWithEmptyProperty.Empty);

            Assert.False(StronglyTypedIntWithEmptyProperty.TryParse(tooLowString, null, out var parsed));
            Assert.Equal(StronglyTypedIntWithEmptyProperty.Empty, parsed);

            Assert.Equal(StronglyTypedIntWithEmptyProperty.Empty, JsonSerializer.Deserialize<StronglyTypedIntWithEmptyProperty>(tooLowString));
        }
    }
}