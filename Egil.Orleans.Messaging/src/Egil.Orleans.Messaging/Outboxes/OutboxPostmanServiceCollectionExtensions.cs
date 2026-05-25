using Egil.Orleans.Messaging.Outboxes;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration helpers for keyed outbox postman services.
/// </summary>
public static class OutboxPostmanServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers a postman implementation using its
        /// <see cref="OutboxPostmanAttribute"/> name.
        /// </summary>
        public IServiceCollection AddOutboxPostman<TPostman>()
            where TPostman : class
        {
            ArgumentNullException.ThrowIfNull(services);

            var name = GetAttributeName(typeof(TPostman));
            return services.AddOutboxPostman<TPostman>(name);
        }

        /// <summary>
        /// Registers all closed <see cref="IPostman{TMessage}"/> contracts
        /// implemented by <typeparamref name="TPostman"/> with the given keyed
        /// postman name.
        /// </summary>
        public IServiceCollection AddOutboxPostman<TPostman>(string postmanName)
            where TPostman : class
        {
            return AddOutboxPostman(services, typeof(TPostman), postmanName);
        }

        /// <summary>
        /// Registers one keyed postman contract.
        /// </summary>
        public IServiceCollection AddOutboxPostman<TMessage, TPostman>(string postmanName)
            where TMessage : notnull
            where TPostman : class, IPostman<TMessage>
        {
            ArgumentNullException.ThrowIfNull(services);
            ValidatePostmanName(postmanName);

            services.TryAddScoped<TPostman>();
            services.AddKeyedScoped<IPostman<TMessage>>(
                postmanName,
                static (provider, _) => provider.GetRequiredService<TPostman>());

            return services;
        }
    }

    internal static IServiceCollection AddOutboxPostman(
        IServiceCollection services,
        Type postmanType,
        string postmanName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(postmanType);
        ValidatePostmanName(postmanName);

        if (postmanType.ContainsGenericParameters)
        {
            throw new ArgumentException("Open generic postman types are not supported.", nameof(postmanType));
        }

        var contracts = GetPostmanContracts(postmanType);
        if (contracts.Length == 0)
        {
            throw new ArgumentException(
                $"Type '{postmanType.FullName}' must implement at least one closed {typeof(IPostman<>).FullName} contract.",
                nameof(postmanType));
        }

        services.TryAddScoped(postmanType);
        foreach (var contract in contracts)
        {
            services.AddKeyedScoped(
                contract,
                postmanName,
                (provider, _) => provider.GetRequiredService(postmanType));
        }

        return services;
    }

    private static string GetAttributeName(Type postmanType)
    {
        var attribute = postmanType.GetCustomAttributes(typeof(OutboxPostmanAttribute), inherit: false)
            .Cast<OutboxPostmanAttribute>()
            .SingleOrDefault();

        if (attribute is null)
        {
            throw new ArgumentException(
                $"Type '{postmanType.FullName}' must declare {nameof(OutboxPostmanAttribute)} or be registered with an explicit postman name.",
                nameof(postmanType));
        }

        ValidatePostmanName(attribute.Name);
        return attribute.Name;
    }

    private static Type[] GetPostmanContracts(Type postmanType) =>
        postmanType.GetInterfaces()
            .Where(static candidate =>
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IPostman<>)
                && !candidate.ContainsGenericParameters)
            .Distinct()
            .ToArray();

    private static void ValidatePostmanName(string postmanName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postmanName);
    }
}