using System.Collections.Immutable;

namespace Egil.Orleans.EventSourcing.Examples;

public class BadWordsDetector
{
    public static string[] BadWords { get; } =
    [
        "BAD",
        "HI",
    ];

    public ValueTask<ImmutableArray<string>> ExtractBadWordsAsync(string message)
    {
        var result = BadWords.Where(badWord => message.Contains(badWord, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToImmutableArray();
        return ValueTask.FromResult(result);
    }
}
