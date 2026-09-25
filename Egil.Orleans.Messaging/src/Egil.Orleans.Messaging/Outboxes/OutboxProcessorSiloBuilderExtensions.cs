using Egil.Orleans.Messaging.Outboxes;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Hosting;

/// <summary>
/// Silo-wide <see cref="OutboxProcessor{TOutbox}"/> defaults on <see cref="ISiloBuilder"/>.
/// </summary>
public static class OutboxProcessorSiloBuilderExtensions
{
    extension(ISiloBuilder builder)
    {
        /// <inheritdoc cref="OutboxProcessorServiceCollectionExtensions.ConfigureOutboxProcessor(IServiceCollection, Action{OutboxProcessorOptions})"/>
        public ISiloBuilder ConfigureOutboxProcessor(Action<OutboxProcessorOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configure);

            builder.ConfigureServices(services => services.ConfigureOutboxProcessor(configure));
            return builder;
        }

        /// <inheritdoc cref="OutboxProcessorServiceCollectionExtensions.ConfigureOutboxProcessor(IServiceCollection, Action{OutboxProcessorOptions, IServiceProvider})"/>
        public ISiloBuilder ConfigureOutboxProcessor(Action<OutboxProcessorOptions, IServiceProvider> configure)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configure);

            builder.ConfigureServices(services => services.ConfigureOutboxProcessor(configure));
            return builder;
        }
    }
}
