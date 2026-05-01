using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace Egil.Orleans.Testing;

/// <summary>
/// Registers <see cref="GrainActivityCollector"/> integration with an Orleans silo.
/// </summary>
public static class GrainActivityCollectorSiloBuilderExtensions
{
    /// <summary>
    /// Adds the supplied <see cref="GrainActivityCollector"/> to the silo and enables incoming grain call monitoring.
    /// </summary>
    /// <param name="builder">The silo builder.</param>
    /// <param name="collector">The collector instance to register.</param>
    /// <returns>A builder that can enable additional sources like storage activity.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="collector"/> is <see langword="null"/>.</exception>
    public static GrainActivityCollectorBuilder AddGrainActivityCollector(this ISiloBuilder builder, GrainActivityCollector collector)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(collector);

        builder.Services.AddSingleton(collector);
        builder.Services.AddSingleton<GrainCallCollectionFilter>();
        builder.AddIncomingGrainCallFilter<GrainCallCollectionFilter>();

        return new GrainActivityCollectorBuilder(builder.Services, collector);
    }
}
