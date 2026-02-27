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

    /// <summary>
    /// Registers a migrator instance type for a single source/target pair.
    /// </summary>
    public JsonMigrationBuilder RegisterMigrator<TSource, TTarget, TMigrator>()
        where TMigrator : class, IMigrate<TSource, TTarget>, new()
    {
        AddRegistration(typeof(TSource), typeof(TTarget), new TMigrator());
        return this;
    }

    /// <summary>
    /// Registers one migrator type for all implemented <see cref="IMigrate{TSource, TTarget}"/> interfaces.
    /// </summary>
    public JsonMigrationBuilder RegisterMigrator<TMigrator>()
        where TMigrator : class, new()
    {
        RegisterMigratorType(typeof(TMigrator));
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

    internal JsonMigrationRegistry Build()
    {
        var byTarget = registrations.Values
            .GroupBy(static registration => registration.TargetType)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToFrozenDictionary(static registration => registration.SourceType));

        return new JsonMigrationRegistry(byTarget.ToFrozenDictionary());
    }

    private void RegisterMigratorType(Type migratorType)
    {
        var contracts = GetMigrationContracts(migratorType).ToArray();
        if (contracts.Length == 0)
        {
            throw new InvalidOperationException($"Type '{migratorType.FullName}' does not implement any IMigrate<,> contracts.");
        }

        if (Activator.CreateInstance(migratorType) is not object migratorInstance)
        {
            throw new InvalidOperationException($"Could not create instance of '{migratorType.FullName}'. Ensure a public parameterless constructor exists.");
        }

        foreach ((Type sourceType, Type targetType) in contracts)
        {
            AddRegistration(sourceType, targetType, migratorInstance);
        }
    }

    private void AddRegistration(Type sourceType, Type targetType, object migratorInstance)
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
                TypeMetadata.FromType(sourceType),
                MigratorInvokerFactory.CreateExternalInvoker(sourceType, targetType, migratorInstance)));
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
