using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.TestingHost;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxReminderFixture : IAsyncLifetime
{
    private InProcessTestCluster cluster = null!;
    public bool ConfigureLifecycle { get; init; } = true;
    public FakeOutboxLogProvider Logs { get; } = new();

    public TGrain GetUniqueGrain<TGrain>() where TGrain : IGrainWithGuidKey =>
        cluster.Client.GetGrain<TGrain>(Guid.NewGuid());
    public FakeFailingReminderTable Reminders =>
        cluster.Silos.Single().ServiceProvider.GetRequiredService<FakeFailingReminderTable>();

    public async ValueTask InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
        builder.ConfigureSilo((_, silo) =>
        {
            if (ConfigureLifecycle)
            {
                silo.ConfigureOutboxProcessor();
            }
            silo.AddMemoryGrainStorage("Payload", options => options.GrainStorageSerializer =
                new global::Orleans.Storage.SystemTextJsonGrainStorageSerializer(
                    Options.Create(new global::Orleans.Serialization.SystemTextJsonGrainStorageSerializerOptions())));
            silo.UseInMemoryReminderService();
            silo.ConfigureServices(services =>
            {
                services.AddLogging(logging => logging.AddProvider(Logs));
                // Keep Orleans' real reminder service and table. Only the table's
                // write boundary rejects selected grains to model a storage outage.
                var registration = services.Single(service => service.ServiceType == typeof(IReminderTable));
                services.Remove(registration);
                services.AddSingleton(provider => new FakeFailingReminderTable(
                    (IReminderTable)(registration.ImplementationInstance
                        ?? registration.ImplementationFactory?.Invoke(provider)
                        ?? ActivatorUtilities.CreateInstance(provider, registration.ImplementationType!))));
                services.AddSingleton<IReminderTable>(provider => provider.GetRequiredService<FakeFailingReminderTable>());
            });
        });
        cluster = builder.Build();
        await cluster.DeployAsync();
    }

    public ValueTask DisposeAsync() => cluster.DisposeAsync();
}

public sealed class FakeOutboxLogProvider : ILoggerProvider
{
    public ConcurrentQueue<OutboxWarning> Warnings { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
    public void Dispose() { }

    private sealed class CaptureLogger(FakeOutboxLogProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Warning
            && category.StartsWith("Egil.Orleans.Messaging.Outboxes.OutboxProcessor", StringComparison.Ordinal);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel) && state is IEnumerable<KeyValuePair<string, object?>> properties)
            {
                owner.Warnings.Enqueue(new OutboxWarning(eventId, formatter(state, exception), exception,
                    properties.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)));
            }
        }
    }
}

public sealed record OutboxWarning(EventId EventId, string Message, Exception? Exception,
    IReadOnlyDictionary<string, object?> Properties);

public sealed class FakeFailingReminderTable(IReminderTable inner) : IReminderTable
{
    private readonly ConcurrentDictionary<GrainId, int> rejected = new();

    public void RejectWrites(GrainId grainId) => rejected.TryAdd(grainId, 0);
    public void AllowWrites(GrainId grainId) => rejected.TryRemove(grainId, out _);
    public int FailedWrites(GrainId grainId) => rejected.GetValueOrDefault(grainId);

    public Task Init() => inner.StartAsync(CancellationToken.None);
    public Task StartAsync(CancellationToken cancellationToken) => inner.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => inner.StopAsync(cancellationToken);
    public Task<ReminderTableData> ReadRows(GrainId grainId) => inner.ReadRows(grainId);
    public Task<ReminderTableData> ReadRows(uint begin, uint end) => inner.ReadRows(begin, end);
    public Task<ReminderEntry?> ReadRow(GrainId grainId, string reminderName) => inner.ReadRow(grainId, reminderName);
    public Task<bool> RemoveRow(GrainId grainId, string reminderName, string eTag) => inner.RemoveRow(grainId, reminderName, eTag);
    public Task TestOnlyClearTable() => inner.TestOnlyClearTable();

    public Task<string?> UpsertRow(ReminderEntry entry)
    {
        if (rejected.TryGetValue(entry.GrainId, out var count))
        {
            rejected[entry.GrainId] = count + 1;
            throw new InvalidOperationException("Reminder storage is unavailable.");
        }

        return inner.UpsertRow(entry);
    }
}
