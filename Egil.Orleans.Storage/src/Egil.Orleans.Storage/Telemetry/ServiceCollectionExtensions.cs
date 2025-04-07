using Egil.Orleans.Storage.Telemetry;
using OpenTelemetry.Trace;
using Orleans.Hosting;
using Orleans.Storage;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring grain storage telemetry in an Orleans application.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds telemetry enrichment for all registered grain storage providers.
    /// </summary>
    /// <param name="siloBuilder">The silo builder instance.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when siloBuilder is null.</exception>
    public static IServiceCollection AddGrainStorageTelemetry(this ISiloBuilder siloBuilder)
    {
        ArgumentNullException.ThrowIfNull(siloBuilder);
        return siloBuilder.Services.AddGrainStorageTelemetry();
    }

    /// <summary>
    /// Adds telemetry enrichment for a specific grain storage provider.
    /// </summary>
    /// <param name="siloBuilder">The silo builder instance.</param>
    /// <param name="name">The name of the storage provider to enrich.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when siloBuilder or name is null.</exception>
    public static IServiceCollection AddGrainStorageTelemetry(
        this ISiloBuilder siloBuilder,
        string name)
    {
        ArgumentNullException.ThrowIfNull(siloBuilder);
        return siloBuilder.Services.AddGrainStorageTelemetry(name);
    }

    /// <summary>
    /// Adds telemetry enrichment for all registered grain storage providers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGrainStorageTelemetry(this IServiceCollection services)
    {
        var grainStorageServiceDescriptors = services
            .Where(x => x.IsKeyedService && x.ServiceType == typeof(IGrainStorage))
            .ToArray();

        foreach (var descriptor in grainStorageServiceDescriptors)
        {
            if (descriptor.ServiceKey is not string key)
            {
                continue;
            }

            services.AddGrainStorageTelemetry(key);
        }

        return services;
    }

    /// <summary>
    /// Adds telemetry enrichment for a specific grain storage provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the storage provider to enrich.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when services or name is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no grain storage provider with the specified name is found.</exception>
    public static IServiceCollection AddGrainStorageTelemetry(
        this IServiceCollection services,
        string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);

        var target =
            services.LastOrDefault(
                x =>
                    x.IsKeyedService
                    && x.ServiceKey?.Equals(name) == true
                    && x.KeyedImplementationFactory is not null
            )
            ?? throw new InvalidOperationException(
                $"No grain storage provider with name '{name}' was found."
            );

        services.Remove(target);
        services.AddKeyedSingleton<IGrainStorage>(
            name,
            (sp, _) =>
            {
                var inner = (IGrainStorage)target.KeyedImplementationFactory!(sp, name);
                return new GrainStorageTelemetryEnricher(name, inner);
            }
        );

        return services;
    }

    /// <summary>
    /// Adds a sampling processor for grain storage telemetry using the silo builder.
    /// </summary>
    /// <param name="siloBuilder">The silo builder instance.</param>
    /// <param name="samplingProbability">The sampling probability between 0.0 and 1.0.</param>
    /// <param name="storageName">Optional storage name to filter telemetry.</param>
    /// <param name="stateName">Optional state name to filter telemetry.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when siloBuilder is null.</exception>
    public static IServiceCollection AddGrainStorageTelemetrySamplingProcessor(
        this ISiloBuilder siloBuilder,
        double samplingProbability,
        string? storageName = null,
        string? stateName = null)
    {
        ArgumentNullException.ThrowIfNull(siloBuilder);

        return siloBuilder.Services.AddGrainStorageTelemetrySamplingProcessor(
            samplingProbability,
            storageName,
            stateName
        );
    }

    /// <summary>
    /// Adds a sampling processor for grain storage telemetry.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="samplingProbability">The sampling probability between 0.0 and 1.0.</param>
    /// <param name="storageName">Optional storage name to filter telemetry.</param>
    /// <param name="stateName">Optional state name to filter telemetry.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGrainStorageTelemetrySamplingProcessor(
        this IServiceCollection services,
        double samplingProbability,
        string? storageName = null,
        string? stateName = null)
    {
        services.ConfigureOpenTelemetryTracerProvider(
            (sp, traceBuilder) =>
                traceBuilder.AddProcessor(
                    new GrainStorageTelemetryProcessor(
                        samplingProbability: samplingProbability,
                        storageName,
                        stateName
                    )
                )
        );

        return services;
    }

    /// <summary>
    /// Adds a processor that marks all grain storage read operations that returns 'not found (404)' as successful in telemetry.
    /// </summary>
    /// <remarks>
    /// Some storage providers, notable Azure Storage, will mark an read from a non-existing entity as an error,
    /// which is not the case for Orleans. Adding this processor will mark such activities as successful.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection GrainStorageReadNotFoundOkTelemetryProcessor(
        this IServiceCollection services)
    {
        services.ConfigureOpenTelemetryTracerProvider((sp, traceBuilder)
            => traceBuilder.AddProcessor(new GrainStorageReadNotFoundOkTelemetryProcessor())
        );

        return services;
    }
}
