using Egil.Orleans.Messaging.State;
using Egil.Orleans.Messaging.State.AzureStorage;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration helpers for Azure Storage-aware state managers.
/// </summary>
public static class AzureStorageStateManagerRegistrationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the Azure Storage-aware keyed
        /// <see cref="IStateManagerFactory"/> for the given storage name.
        /// </summary>
        public IServiceCollection AddAzureStorageStateManager(string storageName)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentException.ThrowIfNullOrWhiteSpace(storageName);

            return services.AddKeyedSingleton(
                typeof(IStateManagerFactory),
                storageName,
                typeof(AzureStorageStateManagerFactory));
        }
    }
}
