using Microsoft.Extensions.DependencyInjection;
using Orleans.Storage;

namespace Egil.Orleans.Testing;

/// <summary>
/// Configures optional activity sources for a <see cref="GrainActivityCollector"/>.
/// </summary>
public sealed class GrainActivityCollectorBuilder
{
    private const string DefaultStorageProviderName = "Default";

    private readonly IServiceCollection services;
    private readonly GrainActivityCollector collector;

    internal GrainActivityCollectorBuilder(IServiceCollection services, GrainActivityCollector collector)
    {
        this.services = services;
        this.collector = collector;
    }

    /// <summary>
    /// Enables storage activity collection for the default storage provider.
    /// </summary>
    /// <returns>The current builder instance.</returns>
    public GrainActivityCollectorBuilder CollectStorageActivityFromDefault() => CollectStorageActivityFrom(DefaultStorageProviderName);

    /// <summary>
    /// Enables storage activity collection for the named storage provider.
    /// </summary>
    /// <param name="providerName">The Orleans storage provider name.</param>
    /// <returns>The current builder instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="providerName"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No keyed <see cref="IGrainStorage"/> registration exists for <paramref name="providerName"/>.</exception>
    public GrainActivityCollectorBuilder CollectStorageActivityFrom(string providerName)
    {
        ArgumentNullException.ThrowIfNull(providerName);

        DecorateGrainStorage(providerName);
        return this;
    }

    private void DecorateGrainStorage(string providerName)
    {
        var descriptor = services.LastOrDefault(candidate =>
            candidate.ServiceType == typeof(IGrainStorage)
            && candidate.IsKeyedService
            && Equals(candidate.ServiceKey, providerName));

        if (descriptor is null)
        {
            throw new InvalidOperationException($"No keyed IGrainStorage registration was found for provider '{providerName}'.");
        }

        var innerKey = $"Egil.Orleans.Testing.Inner::{providerName}";
        if (services.Any(candidate =>
            candidate.ServiceType == typeof(IGrainStorage)
            && candidate.IsKeyedService
            && Equals(candidate.ServiceKey, innerKey)))
        {
            return;
        }

        var index = services.IndexOf(descriptor);
        services.Insert(index, CloneWithNewKey(descriptor, innerKey));
        services.Remove(descriptor);
        services.Insert(
            index + 1,
            ServiceDescriptor.DescribeKeyed(
                typeof(IGrainStorage),
                providerName,
                static (serviceProvider, key) =>
                {
                    var storageName = key?.ToString() ?? throw new InvalidOperationException("Missing grain storage provider key.");
                    return new StorageObserver(
                        serviceProvider.GetRequiredKeyedService<IGrainStorage>($"Egil.Orleans.Testing.Inner::{storageName}"),
                        serviceProvider.GetRequiredService<GrainActivityCollector>(),
                        storageName);
                },
                descriptor.Lifetime));
    }

    private static ServiceDescriptor CloneWithNewKey(ServiceDescriptor descriptor, string newKey)
    {
        if (!descriptor.IsKeyedService)
        {
            throw new InvalidOperationException("Only keyed grain storage registrations can be decorated.");
        }

        if (descriptor.KeyedImplementationInstance is not null)
        {
            return ServiceDescriptor.KeyedSingleton(descriptor.ServiceType, newKey, descriptor.KeyedImplementationInstance);
        }

        if (descriptor.KeyedImplementationType is not null)
        {
            return ServiceDescriptor.DescribeKeyed(descriptor.ServiceType, newKey, descriptor.KeyedImplementationType, descriptor.Lifetime);
        }

        if (descriptor.KeyedImplementationFactory is not null)
        {
            return ServiceDescriptor.DescribeKeyed(descriptor.ServiceType, newKey, descriptor.KeyedImplementationFactory, descriptor.Lifetime);
        }

        throw new InvalidOperationException("The keyed grain storage registration does not expose an implementation to clone.");
    }
}
