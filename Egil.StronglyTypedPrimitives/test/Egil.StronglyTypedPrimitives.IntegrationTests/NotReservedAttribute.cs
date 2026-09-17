#if NET11_0_OR_GREATER
using System.ComponentModel.DataAnnotations;

namespace Examples
{
    // Stands in for an attribute whose answer is only available asynchronously, such as a
    // uniqueness check against a database. It rejects one well-known value so tests can pick a
    // value that fails here and nowhere else.
    public sealed class NotReservedAttribute() : AsyncValidationAttribute("The {0} field must not be reserved.")
    {
        public const string ReservedValue = "reserved";

        protected override async Task<ValidationResult?> IsValidAsync(object? value, ValidationContext validationContext, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return string.Equals(value as string, ReservedValue, StringComparison.OrdinalIgnoreCase)
                ? new ValidationResult(FormatErrorMessage(validationContext.DisplayName))
                : ValidationResult.Success;
        }

        // The synchronous path cannot answer; AsyncValidationAttribute makes it abstract so the
        // subclass decides, and throwing keeps any accidental synchronous evaluation visible.
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
            => throw new NotSupportedException($"{nameof(NotReservedAttribute)} can only validate asynchronously.");
    }
}
#endif