#if NET11_0_OR_GREATER
using System.ComponentModel.DataAnnotations;

namespace Examples
{
    // Stands in for a lookup in an external store: only a fixed list of words is accepted, so the
    // default value fails here as well as in NotBlankAttribute.
    public sealed class KnownWordAttribute() : AsyncValidationAttribute("The {0} field must be a known word.")
    {
        private static readonly string[] KnownWords = ["egil", "alice"];

        protected override async Task<ValidationResult?> IsValidAsync(object? value, ValidationContext validationContext, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return KnownWords.Contains(value as string, StringComparer.Ordinal)
                ? ValidationResult.Success
                : new ValidationResult(FormatErrorMessage(validationContext.DisplayName));
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
            => throw new NotSupportedException($"{nameof(KnownWordAttribute)} can only validate asynchronously.");
    }
}
#endif