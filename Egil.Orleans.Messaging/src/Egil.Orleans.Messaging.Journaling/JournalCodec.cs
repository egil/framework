using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;
using Orleans.Journaling;
using Orleans.Journaling.Json;

namespace Egil.Orleans.Messaging.Journaling;

internal sealed class JournalCodec<TOperation>
{
    public JournalCodec(IOptions<JsonJournalOptions> options)
    {
        // Keep application payload converters and metadata, adding reflection support only
        // to our codecs so registering messaging does not change other journal components.
        var serializerOptions = new JsonSerializerOptions(options.Value.SerializerOptions);
        serializerOptions.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        serializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        Value = new JsonDurableValueCommandCodec<TOperation>(serializerOptions);
    }

    public IDurableValueCommandCodec<TOperation> Value { get; }
}
