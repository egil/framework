#if NET11_0_OR_GREATER
using System.ComponentModel.DataAnnotations;

namespace Examples
{
    // Tells whether the token it receives is the caller's: CancellationToken.None cannot be
    // cancelled, so a generated ValidateAsync that dropped the caller's token would fail here, and
    // a token that is already cancelled surfaces as OperationCanceledException carrying that token.
    public sealed class ForwardsCancellationAttribute() : AsyncValidationAttribute("The {0} field was validated without the caller's cancellation token.")
    {
        protected override async Task<ValidationResult?> IsValidAsync(object? value, ValidationContext validationContext, CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return cancellationToken.CanBeCanceled
                ? ValidationResult.Success
                : new ValidationResult(FormatErrorMessage(validationContext.DisplayName));
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
            => throw new NotSupportedException($"{nameof(ForwardsCancellationAttribute)} can only validate asynchronously.");
    }
}
#endif