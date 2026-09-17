using System.ComponentModel.DataAnnotations;

namespace Egil.StronglyTypedPrimitives
{
    using Examples;

    public class PrimitiveValidatableObjectTest
    {
        [Fact]
        public void Valid_attribute_constrained_value_yields_no_results()
        {
            Assert.Empty(Validate(new StronglyTypedEmailWithValidate("egil@example.com")));
        }

        [Fact]
        public void Every_failing_attribute_is_reported_for_the_default_value()
        {
            // Empty is what an invalid JSON payload or a failed TryParse leaves behind, so it is
            // the value Validate is asked about in practice.
            var results = Validate(StronglyTypedEmailWithValidate.Empty);

            Assert.Collection(
                results,
                result => Assert.Equal(new EmailAddressAttribute().FormatErrorMessage(nameof(StronglyTypedEmailWithValidate)), result.ErrorMessage),
                result => Assert.Equal(new StringLengthAttribute(254) { MinimumLength = 3 }.FormatErrorMessage(nameof(StronglyTypedEmailWithValidate)), result.ErrorMessage));
        }


        [Fact]
        public void Valid_value_with_a_user_written_IsValueValid_yields_no_results()
        {
            Assert.Empty(Validate(new StronglyTypedIntWithConstraintsAndValidate(6)));
        }

        [Fact]
        public void Exception_message_from_a_user_written_IsValueValid_is_reported()
        {
            var results = Validate(StronglyTypedIntWithConstraintsAndValidate.Empty);

            var result = Assert.Single(results);
            Assert.Equal(new ArgumentException("Value must be larger than 5", "value").Message, result.ErrorMessage);
        }

        [Fact]
        public void User_written_IsValueValid_that_returns_false_without_throwing_is_reported()
        {
            var results = Validate(StronglyTypedOddIntWithValidate.Empty);

            var result = Assert.Single(results);
            Assert.Equal("Value is not valid.", result.ErrorMessage);
        }

        [Fact]
        public void Type_without_constraints_yields_no_results()
        {
            Assert.Empty(Validate(StronglyTypedIntWithValidate.Empty));
        }

        // Validator does not recurse into property values, so the strongly typed value is
        // validated directly. The instance is boxed once, at the call site, because
        // ValidationContext requires the same reference it was created with.
        private static List<ValidationResult> Validate(object instance)
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
            return results;
        }
    }
}