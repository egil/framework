using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orleans.Journaling;
using Orleans.Journaling.Json;

namespace Egil.Orleans.Messaging.Journaling.Tests;

public sealed class JournalConfigurationTests
{
    [Fact]
    public void Unsupported_journal_formats_fail_configuration_validation()
    {
        using var host = new HostBuilder().UseOrleans(silo =>
        {
            silo.AddMessagingJournaling();
            silo.Configure<JournaledStateManagerOptions>(options => options.JournalFormatKey = "unsupported");
        }).Build();

        Assert.Throws<OptionsValidationException>(() => host.Services.GetRequiredService<IOptions<JournaledStateManagerOptions>>().Value);
    }

    [Fact]
    public void Codec_preserves_host_converters_without_modifying_host_metadata()
    {
        var options = new JsonJournalOptions();
        options.SerializerOptions.Converters.Add(new PayloadConverter());
        var resolver = options.SerializerOptions.TypeInfoResolver;

        var codec = new JournalCodec<OutboxOperation<Payload>>(Options.Create(options));

        Assert.NotNull(codec.Value);
        Assert.Same(resolver, options.SerializerOptions.TypeInfoResolver);
        Assert.Single(options.SerializerOptions.Converters);
    }

    private sealed record Payload(string Value);

    private sealed class PayloadConverter : JsonConverter<Payload>
    {
        public override Payload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(reader.GetString()!);
        public override void Write(Utf8JsonWriter writer, Payload value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    }
}
