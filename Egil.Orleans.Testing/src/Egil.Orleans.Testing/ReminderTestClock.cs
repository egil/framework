using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.TestingHost;
using TimeProviderExtensions;

namespace Egil.Orleans.Testing;

/// <summary>
/// Provides deterministic time control for reminder tests running in an <see cref="InProcessTestCluster"/>.
/// </summary>
/// <remarks>
/// <para>
/// Create an instance and attach it to an <see cref="InProcessTestClusterBuilder"/> via
/// <see cref="Attach(InProcessTestClusterBuilder, ReminderTestClock)"/> before building the cluster.
/// The attached clock replaces the silo <see cref="TimeProvider"/> with a <see cref="ManualTimeProvider"/>
/// and tunes <see cref="ReminderOptions"/> for deterministic reminder scheduling.
/// </para>
/// <para>
/// After the cluster is deployed, call <see cref="AdvanceAsync"/> to advance time and trigger
/// registered reminders without waiting for real wall-clock time.
/// </para>
/// </remarks>
public sealed class ReminderTestClock : IDisposable
{
    private readonly SemaphoreSlim advanceLock = new(1, 1);
    private bool disposed;

    /// <summary>
    /// Creates a new <see cref="ReminderTestClock"/> with the specified configuration.
    /// </summary>
    /// <param name="minimumReminderPeriod">The minimum reminder period. Defaults to one minute.</param>
    /// <param name="refreshReminderListPeriod">The reminder list refresh period. Defaults to one second.</param>
    /// <param name="initializationTimeout">The reminder service initialization timeout. Defaults to 30 seconds.</param>
    public ReminderTestClock(
        TimeSpan? minimumReminderPeriod = null,
        TimeSpan? refreshReminderListPeriod = null,
        TimeSpan? initializationTimeout = null)
    {
        TimeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        MinimumReminderPeriod = minimumReminderPeriod ?? TimeSpan.FromMinutes(1);
        RefreshReminderListPeriod = refreshReminderListPeriod ?? TimeSpan.FromSeconds(1);
        InitializationTimeout = initializationTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Gets the <see cref="ManualTimeProvider"/> used by this clock.
    /// </summary>
    public ManualTimeProvider TimeProvider { get; }

    /// <summary>
    /// Gets the minimum reminder period configured for clusters using this clock.
    /// </summary>
    public TimeSpan MinimumReminderPeriod { get; }

    /// <summary>
    /// Gets the reminder table refresh period configured for clusters using this clock.
    /// </summary>
    public TimeSpan RefreshReminderListPeriod { get; }

    /// <summary>
    /// Gets the reminder service initialization timeout configured for clusters using this clock.
    /// </summary>
    public TimeSpan InitializationTimeout { get; }

    /// <summary>
    /// Attaches an existing <see cref="ReminderTestClock"/> to the provided <see cref="InProcessTestClusterBuilder"/>.
    /// </summary>
    /// <param name="builder">The test cluster builder.</param>
    /// <param name="clock">The reminder test clock to attach.</param>
    public static void Attach(InProcessTestClusterBuilder builder, ReminderTestClock clock)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(clock);

        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.Services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock.TimeProvider));
            siloBuilder.Services.PostConfigure<ReminderOptions>(options =>
            {
                options.MinimumReminderPeriod = clock.MinimumReminderPeriod;
                options.RefreshReminderListPeriod = clock.RefreshReminderListPeriod;
                options.InitializationTimeout = clock.InitializationTimeout;
            });
        });
    }

    /// <summary>
    /// Advances the cluster reminder clock by the specified amount.
    /// </summary>
    /// <param name="amount">The amount of time to advance.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is negative.</exception>
    public async Task AdvanceAsync(TimeSpan amount, CancellationToken cancellationToken = default)
    {
        if (amount < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "The advance amount must not be negative.");
        }

        await advanceLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            TimeProvider.Advance(amount);
        }
        finally
        {
            advanceLock.Release();
        }

        await Task.Yield();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        advanceLock.Dispose();
    }
}
