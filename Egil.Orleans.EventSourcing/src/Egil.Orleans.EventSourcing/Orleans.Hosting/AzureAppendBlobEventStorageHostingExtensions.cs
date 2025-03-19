using Egil.Orleans.EventSourcing;
using Egil.Orleans.EventSourcing.AzureStorage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace Orleans.Hosting;

public static class AzureAppendBlobEventStorageHostingExtensions
{
    /// <summary>
    /// Adds Azure Append Blob event storage to the silo builder that will be used by <see cref="EventSourcedGrain{TEvent, TState}"/>
    /// to store events.
    /// </summary>
    /// <param name="builder">The builder is used to register services and configure the silo's behavior.</param>
    /// <param name="configure">This action allows customization of the Azure Append Blob event storage options.</param>
    /// <returns>Returns the updated silo builder for further configuration.</returns>
    public static ISiloBuilder AddAzureAppendBlobEventStorage(this ISiloBuilder builder, Action<OptionsBuilder<AzureAppendBlobEventStorageOptions>>? configure = null)
    {
        var services = builder.Services;

        var options = builder.Services.AddOptions<AzureAppendBlobEventStorageOptions>();
        configure?.Invoke(options);

        if (services.Any(service => service.ServiceType.Equals(typeof(AzureAppendBlobEventStorageProvider))))
        {
            return builder;
        }

        builder.Services.AddSingleton<AzureAppendBlobEventStorageProvider>();
        builder.Services.AddSingleton<IAzureAppendBlobEventStorageProvider>(s => s.GetRequiredService<AzureAppendBlobEventStorageProvider>());
        builder.Services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(s => s.GetRequiredService<AzureAppendBlobEventStorageProvider>());

        return builder;
    }

    /// <summary>
    /// Adds Azure Append Blob event storage to the silo builder with optional configuration options.
    /// </summary>
    /// <param name="builder">Used to configure the silo with Azure Append Blob event storage capabilities.</param>
    /// <param name="name">Specifies the name of the connection string to retrieve for Azure Blob storage.</param>
    /// <param name="configureOptions">Allows additional configuration options for the Azure Append Blob event storage.</param>
    /// <returns>Returns the updated silo builder with the added Azure Append Blob event storage.</returns>
    public static ISiloBuilder AddAzureAppendBlobEventStorage(this ISiloBuilder builder, string name, Action<AzureAppendBlobEventStorageOptions>? configureOptions = null)
    {
        return builder.AddAzureAppendBlobEventStorage(configure =>
        {
            configure.Configure(options =>
            {
                var connectionString = builder.Configuration.GetConnectionString(name);
                if (!string.IsNullOrEmpty(connectionString))
                {
                    if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
                    {
                        options.ConfigureBlobServiceClient(uri);
                    }
                    else
                    {
                        options.ConfigureBlobServiceClient(connectionString: connectionString);
                    }
                }
            });

            if (configureOptions is not null)
            {
                configure.Configure(configureOptions);
            }
        });
    }
}
