using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Egil.StronglyTypedPrimitives
{
    using Examples;

    public class PrimitiveValidationAttributeTest
    {
        [Fact]
        public void EmailAddress_attribute_rejects_values_that_are_not_email_addresses()
        {
            var exception = Assert.Throws<ValidationException>(() => new StronglyTypedEmail("not-an-email"));

            Assert.Equal(new EmailAddressAttribute().FormatErrorMessage("Value"), exception.Message);
            Assert.Equal("not-an-email", exception.Value);
            Assert.Equal("egil@example.com", new StronglyTypedEmail("egil@example.com").Value);
        }

        [Fact]
        public void Range_attribute_rejects_values_outside_the_range()
        {
            Assert.Throws<ValidationException>(() => new StronglyTypedIntWithRange(5));
            Assert.Throws<ValidationException>(() => new StronglyTypedIntWithRange(101));
            Assert.Equal(6, new StronglyTypedIntWithRange(6).Value);
            Assert.Equal(100, new StronglyTypedIntWithRange(100).Value);
        }

        [Fact]
        public void StringLength_attribute_rejects_values_outside_the_length_bounds()
        {
            Assert.Throws<ValidationException>(() => new StronglyTypedStringWithLength("a"));
            Assert.Throws<ValidationException>(() => new StronglyTypedStringWithLength("abcdefghijk"));
            Assert.Equal("ab", new StronglyTypedStringWithLength("ab").Value);
            Assert.Equal("abcdefghij", new StronglyTypedStringWithLength("abcdefghij").Value);
        }

        [Fact]
        public void Required_attribute_rejects_null_and_empty_values()
        {
            Assert.Throws<ValidationException>(() => new StronglyTypedRequiredString(null!));
            Assert.Throws<ValidationException>(() => new StronglyTypedRequiredString(string.Empty));
            Assert.Equal("x", new StronglyTypedRequiredString("x").Value);
        }

        [Fact]
        public void Custom_validation_attribute_is_evaluated_like_the_built_in_ones()
        {
            var exception = Assert.Throws<ValidationException>(() => new StronglyTypedUpperCaseString("abc"));

            Assert.Equal("The Value field must be upper case.", exception.Message);
            Assert.Equal("ABC", new StronglyTypedUpperCaseString("ABC").Value);
        }

        [Fact]
        public void Attribute_overriding_only_the_context_IsValid_is_evaluated_with_a_context()
        {
            var exception = Assert.Throws<ValidationException>(() => new StronglyTypedNotBlank("   "));

            Assert.Equal("The Value field must not be blank.", exception.Message);
            Assert.Equal("x", new StronglyTypedNotBlank("x").Value);
            Assert.False(StronglyTypedNotBlank.TryParse("   ", null, out _));
        }

        [Fact]
        public void Each_IsValueValid_call_gets_a_ValidationContext_of_its_own()
        {
            var results = new bool[1000];

            Parallel.For(0, results.Length, i => results[i] = StronglyTypedContextItemsChecked.IsValueValid($"value{i}", throwIfInvalid: false));

            Assert.All(results, result => Assert.True(result));
            Assert.Equal("x", new StronglyTypedContextItemsChecked("x").Value);
            Assert.Equal("y", new StronglyTypedContextItemsChecked("y").Value);
        }

        [Fact]
        public void Failure_without_an_error_message_is_still_a_failure()
        {
            Assert.Throws<ValidationException>(() => new StronglyTypedNullMessageChecked("bad"));
            Assert.False(StronglyTypedNullMessageChecked.TryParse("bad", null, out _));
            Assert.Equal("ok", new StronglyTypedNullMessageChecked("ok").Value);
        }

        [Fact]
        public void Constructor_reports_every_failing_attribute_in_declaration_order()
        {
            var expectedMessage = new EmailAddressAttribute().FormatErrorMessage("Value")
                + Environment.NewLine
                + new StringLengthAttribute(20) { MinimumLength = 6 }.FormatErrorMessage("Value");

            var exception = Assert.Throws<ValidationException>(() => new StronglyTypedShortEmail("abc"));

            Assert.Equal(expectedMessage, exception.Message);
            Assert.Equal("abc", exception.Value);
        }

        [Fact]
        public void IsValueValid_returns_false_instead_of_throwing_when_asked_not_to_throw()
        {
            Assert.False(StronglyTypedShortEmail.IsValueValid("abc", throwIfInvalid: false));
            Assert.True(StronglyTypedShortEmail.IsValueValid("egil@example.com", throwIfInvalid: false));
        }

        [Fact]
        public void TryParse_returns_false_for_an_invalid_value()
        {
            Assert.False(StronglyTypedShortEmail.TryParse("abc", null, out var parsed));
            Assert.Equal(StronglyTypedShortEmail.Empty, parsed);
        }

        [Fact]
        public void Json_deserialization_of_an_invalid_value_yields_Empty()
        {
            Assert.Equal(StronglyTypedShortEmail.Empty, JsonSerializer.Deserialize<StronglyTypedShortEmail>("\"abc\""));
            Assert.Equal(StronglyTypedIntWithRange.Empty, JsonSerializer.Deserialize<StronglyTypedIntWithRange>("5"));
        }

        [Fact]
        public void With_expression_with_an_invalid_value_throws()
        {
            var email = new StronglyTypedShortEmail("egil@example.com");

            Assert.Throws<ValidationException>(() => email with { Value = "abc" });
        }

        [Fact]
        public void User_static_initializer_built_through_the_constructor_does_not_break_type_initialization()
        {
            Assert.Equal(1, StronglyTypedQuantity.Default.Value);
            Assert.Equal(50, new StronglyTypedQuantity(50).Value);
            Assert.Throws<ValidationException>(() => new StronglyTypedQuantity(0));
        }

        [Fact]
        public void User_declared_Empty_built_through_the_constructor_is_the_empty_value()
        {
            Assert.Equal(1, StronglyTypedPositiveInt.Empty.Value);
            Assert.False(StronglyTypedPositiveInt.TryParse("0", null, out var parsed));
            Assert.Equal(StronglyTypedPositiveInt.Empty, parsed);
        }

        [Fact]
        public void Valid_value_round_trips_through_json()
        {
            var email = new StronglyTypedShortEmail("egil@example.com");

            var json = JsonSerializer.Serialize(email);

            Assert.Equal("\"egil@example.com\"", json);
            Assert.Equal(email, JsonSerializer.Deserialize<StronglyTypedShortEmail>(json));
        }
    }
}
