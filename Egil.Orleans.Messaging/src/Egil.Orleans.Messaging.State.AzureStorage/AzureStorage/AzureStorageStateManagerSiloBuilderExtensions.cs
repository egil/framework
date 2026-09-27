using Microsoft.Extensions.DependencyInjection;
using Egil.Orleans.Messaging.State;

namespace Orleans.Hosting;

/// <summary>
/// Silo builder registration helpers for Azure Storage-aware state managers.
/// </summary>
public static class AzureStorageStateManagerSiloBuilderExtensions
{
    extension(ISiloBuilder builder)
    {
        /// <summary>
        /// Registers the Azure Storage-aware state manager factory on the silo builder.
        /// </summary>
        public ISiloBuilder AddAzureStorageStateManager(string storageName, Action<StateManagerOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(storageName);

            builder.ConfigureServices(services => services.AddAzureStorageStateManager(storageName, configure));
            return builder;
        }
    }
}