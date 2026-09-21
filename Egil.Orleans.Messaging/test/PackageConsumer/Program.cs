using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Egil.Orleans.Messaging.Journaling;
using Egil.Orleans.Messaging.Outboxes;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.Runtime;
using Orleans.TestingHost;

if (args.Length != 2 || args[0] is not ("write" or "read") || string.IsNullOrWhiteSpace(args[1]))
{
    Console.Error.WriteLine("Usage: PackageConsumer <write|read> <journal-directory>");
    Environment.ExitCode = 2;
    return;
}

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
var directory = Path.GetFullPath(args[1]);
Directory.CreateDirectory(directory);
var builder = new InProcessTestClusterBuilder(1);
builder.ConfigureSilo((_, silo) =>
{
    silo.UseInMemoryReminderService();
    silo.UseJsonJournalFormat(options =>
    {
        options.AddTypeInfoResolver(new DefaultJsonTypeInfoResolver());
        options.SerializerOptions.Converters.Add(new PayloadConverter());
    });
    silo.AddMessagingJournaling();
    silo.Services.AddSingleton<IJournalStorageProvider>(new FileJournalProvider(directory));
});
await using var cluster = builder.Build();
await cluster.DeployAsync().WaitAsync(timeout.Token);
var grain = cluster.Client.GetGrain<ISmokeGrain>(Guid.Empty);
if (args[0] == "write")
{
    var revision = await grain.WriteAsync().WaitAsync(timeout.Token);
    await File.WriteAllTextAsync(Path.Combine(directory, "revision"), revision.ToString(), timeout.Token);
}
else
{
    var expected = Guid.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "revision"), timeout.Token));
    if (await grain.VerifyAsync().WaitAsync(timeout.Token) != expected)
        throw new InvalidOperationException("Restart changed the stored outbox revision.");
}
Console.WriteLine($"Package consumer {args[0]} passed.");

public interface ISmokeGrain : IGrainWithGuidKey
{
    Task<Guid> WriteAsync();
    Task<Guid> VerifyAsync();
}

public sealed class SmokeGrain(
    [FromKeyedServices("business")] IDurableValue<int> business,
    [FromKeyedServices("receiver")] IDurableMessageTracker tracker,
    [FromKeyedServices("messages")] IDurableOutbox<Payload> outbox) : DurableGrain, ISmokeGrain, IOutboxGrain
{
    private static OutboxSequenceToken Token => new(1, GrainId.Create("sender", "one"), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    public async Task<Guid> WriteAsync()
    {
        if (!tracker.TryAcceptMessage(Token)) throw new InvalidOperationException("Unexpected duplicate.");
        business.Value = 17;
        outbox.Add(new Payload("durable"));
        await WriteStateAsync();
        return outbox.Revision;
    }

    public async Task<Guid> VerifyAsync()
    {
        if (business.Value != 17 || tracker.TryAcceptMessage(Token) || outbox.Single().Value != "durable")
            throw new InvalidOperationException("Shared journal recovery failed.");
        var revision = outbox.Revision;
        var captured = outbox.AsImmutable();
        var delivered = false;
        var processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<Payload>
        {
            OutboxAccessor = () => outbox.AsImmutable(),
            AcknowledgePostedAsync = async (items, cancellationToken) =>
            {
                outbox.RemoveRange(items);
                await WriteStateAsync(cancellationToken);
            }
        }).AddPostman<Payload>(_ => { delivered = true; return ValueTask.CompletedTask; });
        await processor.PostAsync();
        if (!delivered || outbox.Count != 0 || captured.Count != 1)
            throw new InvalidOperationException("Immutable processor integration failed.");
        return revision;
    }
}

[GenerateSerializer]
public sealed record Payload([property: Id(0)] string Value);

public sealed class PayloadConverter : JsonConverter<Payload>
{
    public override Payload Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var value = reader.GetString()!;
        if (!value.StartsWith("custom:", StringComparison.Ordinal)) throw new JsonException("Custom converter was bypassed.");
        return new(value[7..]);
    }
    public override void Write(Utf8JsonWriter writer, Payload value, JsonSerializerOptions options) => writer.WriteStringValue("custom:" + value.Value);
}

// Test-only single-process provider. Each write atomically replaces a file; it is not
// a distributed store and deliberately provides no multi-silo fencing or locking.
public sealed class FileJournalProvider(string directory) : IJournalStorageProvider
{
    public IJournalStorage CreateStorage(JournalId id) => new FileJournalStorage(Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id.ToString()))) + ".journal"));
}

public sealed class FileJournalStorage(string path) : IJournalStorage
{
    public bool IsCompactionRequested => false;
    public async ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
    {
        var memory = new VolatileJournalStorage(JsonJournalExtensions.JournalFormatKey);
        if (File.Exists(path))
            await memory.AppendAsync(new ReadOnlySequence<byte>(await File.ReadAllBytesAsync(path, cancellationToken)), cancellationToken);
        await memory.ReadAsync(consumer, cancellationToken);
    }
    public async ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
    {
        var previous = File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : [];
        await ReplaceAsync(new ReadOnlySequence<byte>(previous.Concat(value.ToArray()).ToArray()), cancellationToken);
    }
    public async ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            foreach (var segment in value) await stream.WriteAsync(segment, cancellationToken);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }
    public ValueTask DeleteAsync(CancellationToken cancellationToken)
    {
        File.Delete(path);
        return ValueTask.CompletedTask;
    }
}
