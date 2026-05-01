using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Hosting;

/// <summary>
/// Registers <see cref="Egil.Orleans.Testing.GrainActivityCollector"/> integration with an Orleans silo.
/// </summary>
public static class GrainActivityCollectorSiloBuilderExtensions
{
    /// <summary>
    /// Adds the supplied <see cref="Egil.Orleans.Testing.GrainActivityCollector"/> to the silo and enables incoming grain call monitoring.
    /// </summary>
    /// <param name="builder">The silo builder.</param>
    /// <param name="collector">The collector instance to register.</param>
    /// <returns>A builder that can enable additional sources like storage activity.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="collector"/> is <see langword="null"/>.</exception>
    public static Egil.Orleans.Testing.GrainActivityCollectorBuilder AddGrainActivityCollector(
        this ISiloBuilder builder,
        Egil.Orleans.Testing.GrainActivityCollector collector)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(collector);

        builder.Services.AddSingleton(collector);
        builder.Services.AddSingleton<Egil.Orleans.Testing.GrainCallCollectionFilter>();
        builder.AddIncomingGrainCallFilter<Egil.Orleans.Testing.GrainCallCollectionFilter>();

        return new Egil.Orleans.Testing.GrainActivityCollectorBuilder(builder.Services, collector);
    }
}
