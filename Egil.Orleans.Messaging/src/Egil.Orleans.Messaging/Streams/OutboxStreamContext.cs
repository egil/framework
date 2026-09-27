using System.Text.Json;
using Egil.Orleans.Messaging.Outboxes;

namespace Egil.Orleans.Messaging.Streams;

internal static class OutboxStreamContext
{
    internal const string Key = "egil.orleans.messaging.outbox";

    internal static string Encode(OutboxSequenceToken token)
    {
        Validate(token);
        return "v1:" + JsonSerializer.Serialize(token);
    }

    internal static OutboxSequenceToken? Read()
    {
        if (!RequestContext.Keys.Contains(Key))
            return null;

        // The wire value is a primitive string: v1:{"SequenceNumber":1,"Sender":{...},...}.
        // A present invalid entry must fault delivery instead of silently weakening deduplication.
        if (RequestContext.Get(Key) is not string encoded || !encoded.StartsWith("v1:", StringComparison.Ordinal))
            throw new JsonException("Invalid or unsupported outbox stream identity in request context.");
        var token = JsonSerializer.Deserialize<OutboxSequenceToken>(encoded.AsSpan(3))
            ?? throw new JsonException("Outbox stream identity must not be null.");
        Validate(token);
        return token;
    }

    internal static void Validate(OutboxSequenceToken token)
    {
        if (token.Sender.IsDefault || token.SequenceNumber <= 0)
            throw new JsonException("Outbox stream identity requires a non-default sender and a positive sequence number.");
    }
}
