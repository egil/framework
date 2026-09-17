#if NET11_0_OR_GREATER
using System.ComponentModel.DataAnnotations;

namespace Egil.StronglyTypedPrimitives
{
    using Examples;

    public class PrimitiveAsyncValidatableObjectTest
    {
        [Fact]
        public async Task Valid_value_yields_no_results()
        {
            Assert.Empty(await ValidateAsync(new StronglyTypedUsername("egil")));
        }

        [Fact]
        public async Task Failing_sync_attribute_is_reported_for_the_default_value()
        {
            var results = await ValidateAsync(StronglyTypedUsername.Empty);

            var result = Assert.Single(results);
            Assert.Equal(new StringLengthAttribute(10) { MinimumLength = 2 }.FormatErrorMessage(nameof(StronglyTypedUsername)), result.ErrorMessage);
        }

        [Fact]
        public async Task Failing_async_attribute_is_reported()
        {
            var results = await ValidateAsync(new StronglyTypedUsername(NotReservedAttribute.ReservedValue));

            var result = Assert.Single(results);
            Assert.Equal(new NotReservedAttribute().FormatErrorMessage(nameof(StronglyTypedUsername)), result.ErrorMessage);
        }

        // Only the default value can fail the sync attribute, because the constructor enforces it;
        // Empty is what an invalid payload leaves behind. Both async attributes reject it too, so
        // all three fail and the order they were declared in is the order reported: the sync
        // result from Validate first, then each async attribute in turn.
        [Fact]
        public async Task Every_failing_attribute_is_reported_in_declaration_order()
        {
            var results = await ValidateAsync(StronglyTypedDisplayName.Empty);

            Assert.Collection(
                results,
                result => Assert.Equal(new StringLengthAttribute(6) { MinimumLength = 2 }.FormatErrorMessage(nameof(StronglyTypedDisplayName)), result.ErrorMessage),
                result => Assert.Equal(new NotBlankAsyncAttribute().FormatErrorMessage(nameof(StronglyTypedDisplayName)), result.ErrorMessage),
                result => Assert.Equal(new KnownWordAttribute().FormatErrorMessage(nameof(StronglyTypedDisplayName)), result.ErrorMessage));
        }

        // The three tests below call the generated ValidateAsync directly so the token it forwards
        // is exactly the one under test, and enumerate through Collect rather than WithCancellation:
        // [EnumeratorCancellation] would link an enumeration token with the one passed to
        // ValidateAsync, and the attribute would then see that linked token instead.
        // ForwardsCancellationAttribute fails for a token that cannot be cancelled, so a valid
        // result with the caller's token proves the token reached it.
        [Fact]
        public async Task Callers_cancellation_token_reaches_the_async_attribute()
        {
            using var cancellation = new CancellationTokenSource();
            var name = new StronglyTypedCancellableName("egil");

            var results = await Collect(name.ValidateAsync(new ValidationContext(name), cancellation.Token));

            Assert.Empty(results);
        }

        [Fact]
        public async Task Attribute_can_tell_when_no_cancellable_token_was_forwarded()
        {
            var name = new StronglyTypedCancellableName("egil");

            var results = await Collect(name.ValidateAsync(new ValidationContext(name), CancellationToken.None));

            var result = Assert.Single(results);
            Assert.Equal(new ForwardsCancellationAttribute().FormatErrorMessage(nameof(StronglyTypedCancellableName)), result.ErrorMessage);
        }

        [Fact]
        public async Task Cancelled_token_cancels_the_async_attribute_with_that_token()
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            var name = new StronglyTypedCancellableName("egil");

            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => Collect(name.ValidateAsync(new ValidationContext(name), cancellation.Token)));

            Assert.Equal(cancellation.Token, exception.CancellationToken);
        }

        // The async attribute is not part of the value invariant: constructing the value, and the
        // synchronous Validate, only apply the attributes IsValueValid can evaluate.
        [Fact]
        public void Async_attribute_is_not_part_of_the_value_invariant()
        {
            var username = new StronglyTypedUsername(NotReservedAttribute.ReservedValue);

            Assert.Equal(NotReservedAttribute.ReservedValue, username.Value);
            Assert.Empty(username.Validate(new ValidationContext(username)));
        }

        private static async Task<List<ValidationResult>> Collect(IAsyncEnumerable<ValidationResult> results)
        {
            var list = new List<ValidationResult>();
            await foreach (var result in results)
            {
                list.Add(result);
            }

            return list;
        }

        // Validator does not recurse into property values, so the strongly typed value is
        // validated directly. The instance is boxed once, at the call site, because
        // ValidationContext requires the same reference it was created with.
        private static async Task<List<ValidationResult>> ValidateAsync(object instance)
        {
            var results = new List<ValidationResult>();
            await Validator.TryValidateObjectAsync(instance, new ValidationContext(instance), results, validateAllProperties: true, TestContext.Current.CancellationToken);
            return results;
        }
    }
}
#endif