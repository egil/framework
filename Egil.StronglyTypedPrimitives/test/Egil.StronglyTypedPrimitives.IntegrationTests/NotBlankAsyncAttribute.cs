#if NET11_0_OR_GREATER
using System.ComponentModel.DataAnnotations;

namespace Examples
{
    // A second async attribute that, unlike NotReservedAttribute, rejects the default value, so a
    // test can make the sync attribute and two async attributes fail on the same value and check
    // the order they are reported in. The async twin of the synchronous NotBlankAttribute.
    public sealed class NotBlankAsyncAttribute() : AsyncValidationAttribute("The {0} field must not be blank.")
    {
        protected override async Task<ValidationResult?> IsValidAsync(object? value, ValidationContext validationContext, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return string.IsNullOrWhiteSpace(value as string)
                ? new ValidationResult(FormatErrorMessage(validationContext.DisplayName))
                : ValidationResult.Success;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
            => throw new NotSupportedException($"{nameof(NotBlankAsyncAttribute)} can only validate asynchronously.");
    }
}
#endif