#if NET11_0_OR_GREATER
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;

namespace Examples
{
    // Always passes and counts how often it was asked about each value, so a test can tell how
    // many times a validation pipeline evaluates an async attribute. The counts are keyed by value
    // rather than kept in one counter so tests that run in parallel each read their own value.
    public sealed class InvocationCountingAttribute() : AsyncValidationAttribute("The {0} field was counted.")
    {
        private static readonly ConcurrentDictionary<string, int> invocations = new(StringComparer.Ordinal);

        public static int InvocationsFor(string value) => invocations.GetValueOrDefault(value);

        protected override async Task<ValidationResult?> IsValidAsync(object? value, ValidationContext validationContext, CancellationToken cancellationToken)
        {
            await Task.Yield();
            invocations.AddOrUpdate((string?)value ?? string.Empty, 1, static (_, count) => count + 1);
            return ValidationResult.Success;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
            => throw new NotSupportedException($"{nameof(InvocationCountingAttribute)} can only validate asynchronously.");
    }
}
#endif