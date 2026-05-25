using Microsoft.Extensions.DependencyInjection;

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
        public ISiloBuilder AddAzureStorageStateManager(string storageName)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(storageName);

            builder.ConfigureServices(services => services.AddAzureStorageStateManager(storageName));
            return builder;
        }
    }
}