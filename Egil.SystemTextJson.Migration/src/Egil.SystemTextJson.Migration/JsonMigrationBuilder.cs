using System.Collections.Frozen;
using System.Reflection;
using Egil.SystemTextJson.Migration.Migrations;

namespace Egil.SystemTextJson.Migration;

/// <summary>
/// Builds migration registrations used by JSON migration support.
/// </summary>
public sealed class JsonMigrationBuilder
{
    private readonly Dictionary<(Type Source, Type Target), ExternalMigratorRegistration> registrations = new();
    private Func<Type, string?>? typeDiscriminatorResolver;
    private string? typeDiscriminatorPropertyName;
    private IServiceProvider? migratorServiceProvider;
    private JsonMigrationFailureHandling migrationFailureHandling = JsonMigrationFailureHandling.ThrowJsonException;

    /// <summary>
    /// Registers a migrator instance type for a single source/target pair.
    /// </summary>
    public JsonMigrationBuilder RegisterMigrator<TSource, TTarget, TMigrator>()
        where TMigrator : class, IMigrate<TSource, TTarget>
    {
        AddRegistration(typeof(TSource), typeof(TTarget), typeof(TMigrator));
        return this;
    }

    /// <summary>
    /// Registers one migrator type for all implemented <see cref="IMigrate{TSource, TTarget}"/> interfaces.
    /// </summary>
    public JsonMigrationBuilder RegisterMigrator<TMigrator>()
        where TMigrator : class
    {
        RegisterMigratorType(typeof(TMigrator));
        return this;
    }

    /// <summary>
    /// Configures a service provider used to instantiate migrator types.
    /// If a migrator type is not registered in the provider, activation falls back to an accessible
    /// parameterless constructor.
    /// </summary>
    public JsonMigrationBuilder UseServiceProvider(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        migratorServiceProvider = serviceProvider;
        return this;
    }

    /// <summary>
    /// Scans one assembly for migrator types.
    /// </summary>
    public JsonMigrationBuilder RegisterMigratorsFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (Type type in GetLoadableTypes(assembly))
        {
            if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters)
            {
                continue;
            }

            if (!GetMigrationContracts(type).Any())
            {
                continue;
            }

            RegisterMigratorType(type);
        }

        return this;
    }

    /// <summary>
    /// Scans multiple assemblies for migrator types.
    /// </summary>
    public JsonMigrationBuilder RegisterMigratorsFromAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (Assembly assembly in assemblies)
        {
            RegisterMigratorsFromAssembly(assembly);
        }

        return this;
    }

    /// <summary>
    /// Configures a type discriminator source based on a custom attribute.
    /// Falls back to <see cref="JsonMigratableAttribute.TypeDiscriminator"/> when the custom attribute is absent.
    /// </summary>
    public JsonMigrationBuilder GetTypeDiscriminatorFrom<TAttribute>(Func<TAttribute, string?> selector)
        where TAttribute : Attribute
    {
        ArgumentNullException.ThrowIfNull(selector);

        typeDiscriminatorResolver = type =>
        {
            TAttribute? attribute = type.GetCustomAttribute<TAttribute>(inherit: false);
            return attribute is null ? null : selector(attribute);
        };

        return this;
    }

    /// <summary>
    /// Sets the default discriminator property name used when a type does not specify one on
    /// <see cref="JsonMigratableAttribute"/>.
    /// </summary>
    public JsonMigrationBuilder SetTypeDiscriminatorPropertyName(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException("Type discriminator property name cannot be null, empty, or whitespace.", nameof(propertyName));
        }

        typeDiscriminatorPropertyName = propertyName;
        return this;
    }

    /// <summary>
    /// Sets how deserialization should handle migrators that return <c>false</c> from <c>TryMigrateFrom</c>.
    /// </summary>
    public JsonMigrationBuilder SetMigrationFailureHandling(JsonMigrationFailureHandling handling)
    {
        if (!Enum.IsDefined(handling))
        {
            throw new ArgumentOutOfRangeException(nameof(handling), handling, $"Value must be one of {nameof(JsonMigrationFailureHandling.ThrowJsonException)}, {nameof(JsonMigrationFailureHandling.FallBackToTargetType)}, or {nameof(JsonMigrationFailureHandling.ReturnNull)}.");
        }

        migrationFailureHandling = handling;
        return this;
    }

    internal JsonMigrationRegistry Build()
    {
        Func<Type, string?>? resolver = typeDiscriminatorResolver;
        string? defaultDiscriminatorPropertyName = typeDiscriminatorPropertyName;

        var byTarget = registrations.Values
            .Select(registration => registration with
            {
                SourceMetadata = TypeMetadata.FromType(registration.SourceType, resolver, defaultDiscriminatorPropertyName),
            })
            .GroupBy(static registration => registration.TargetType)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToFrozenDictionary(static registration => registration.SourceType));

        return new JsonMigrationRegistry(byTarget.ToFrozenDictionary(), resolver, defaultDiscriminatorPropertyName, migrationFailureHandling);
    }

    private void RegisterMigratorType(Type migratorType)
    {
        var contracts = GetMigrationContracts(migratorType).ToArray();
        if (contracts.Length == 0)
        {
            throw new InvalidOperationException($"Type '{migratorType.FullName}' does not implement any IMigrate<,> contracts.");
        }

        foreach ((Type sourceType, Type targetType) in contracts)
        {
            AddRegistration(sourceType, targetType, migratorType);
        }
    }

    private void AddRegistration(Type sourceType, Type targetType, Type migratorType)
    {
        var key = (Source: sourceType, Target: targetType);
        if (registrations.ContainsKey(key))
        {
            throw new InvalidOperationException($"Duplicate migrator registration for ({sourceType.FullName} -> {targetType.FullName}).");
        }

        registrations.Add(
            key,
            new ExternalMigratorRegistration(
                sourceType,
                targetType,
                TypeMetadata.FromType(sourceType, typeDiscriminatorResolver, typeDiscriminatorPropertyName),
                MigratorInvokerFactory.CreateExternalInvoker(sourceType, targetType, migratorType, migratorServiceProvider)));
    }

    private static IEnumerable<(Type Source, Type Target)> GetMigrationContracts(Type type)
    {
        foreach (Type @interface in type.GetInterfaces())
        {
            if (!@interface.IsGenericType || @interface.GetGenericTypeDefinition() != typeof(IMigrate<,>) || @interface.ContainsGenericParameters)
            {
                continue;
            }

            Type[] args = @interface.GetGenericArguments();
            yield return (args[0], args[1]);
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(static type => type is not null)!;
        }
    }
}
