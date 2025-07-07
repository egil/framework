using System.Collections.Immutable;

namespace Egil.Orleans.EventSourcing.Examples;

public interface IBadWordsDetector
{
    ValueTask<ImmutableArray<string>> ExtractBadWordsAsync(string message);
}
