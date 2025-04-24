using Egil.Orleans.EventSourcing;
using Egil.Orleans.EventSourcing.AzureStorage.TableStorage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace Orleans.Hosting;

public static class AzureTableEventStorageHostingExtensions
{
    /// <summary>
    /// Adds Azure Table event storage to the silo builder that will be used by <see cref="EventSourcedGrain{TEvent, TState}"/>
    /// to store events.
    /// </summary>
    /// <param name="builder">The builder is used to register services and configure the silo's behavior.</param>
    /// <param name="configure">This action allows customization of the Azure Table event storage options.</param>
    /// <returns>Returns the updated silo builder for further configuration.</returns>
    public static ISiloBuilder AddAzureTableEventStorage(this ISiloBuilder builder, Action<OptionsBuilder<AzureTableEventStorageOptions>>? configure = null)
    {
        var services = builder.Services;

        var options = builder.Services.AddOptions<AzureTableEventStorageOptions>();
        configure?.Invoke(options);

        if (services.Any(service => service.ServiceType.Equals(typeof(StreamstoneEventStorageProvider))))
        {
            return builder;
        }

        builder.Services.AddSingleton<StreamstoneEventStorageProvider>();
        builder.Services.AddSingleton<IEventStorageProvider>(s => s.GetRequiredService<StreamstoneEventStorageProvider>());
        builder.Services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(s => s.GetRequiredService<StreamstoneEventStorageProvider>());

        return builder;
    }

    /// <summary>
    /// Adds Azure Table event storage to the silo builder with optional configuration options.
    /// </summary>
    /// <param name="builder">Used to configure the silo with Azure Table event storage capabilities.</param>
    /// <param name="name">Specifies the name of the connection string to retrieve for Azure Table storage.</param>
    /// <param name="configureOptions">Allows additional configuration options for the Azure Table event storage.</param>
    /// <returns>Returns the updated silo builder with the added Azure Table event storage.</returns>
    public static ISiloBuilder AddAzureTableEventStorage(this ISiloBuilder builder, string name, Action<AzureTableEventStorageOptions>? configureOptions = null)
    {
        return builder.AddAzureTableEventStorage(configure =>
        {
            configure.Configure(options =>
            {
                var connectionString = builder.Configuration.GetConnectionString(name);
                if (!string.IsNullOrEmpty(connectionString))
                {
                    if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
                    {
                        options.TableServiceClient = new Azure.Data.Tables.TableServiceClient(uri);
                    }
                    else
                    {
                        options.TableServiceClient = new Azure.Data.Tables.TableServiceClient(connectionString: connectionString);
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
