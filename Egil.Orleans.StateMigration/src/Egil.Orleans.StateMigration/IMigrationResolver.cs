namespace Egil.Orleans.StateMigration;

/// <summary>
/// Resolves and executes a direct migration between a source and target state type.
/// </summary>
/// <remarks>
/// Resolution order is:
/// 1. Static <see cref="IMigrateFrom{TSource, TTarget}"/> on the target type.
/// 2. External <see cref="IMigrate{TSource, TTarget}"/> from dependency injection.
/// If no migration exists, or external registrations are ambiguous, an exception is thrown.
/// </remarks>
/// <example>
/// <code><![CDATA[
/// var resolver = serviceProvider.GetRequiredService<IMigrationResolver>();
/// CurrentState migrated = resolver.Migrate<LegacyState, CurrentState>(legacy);
/// ]]></code>
/// </example>
public interface IMigrationResolver
{
    /// <summary>
    /// Migrates <typeparamref name="TSource"/> to <typeparamref name="TTarget"/> using the configured
    /// migration strategy.
    /// </summary>
    /// <typeparam name="TSource">The source state type.</typeparam>
    /// <typeparam name="TTarget">The target state type.</typeparam>
    /// <param name="source">The source instance.</param>
    /// <returns>The migrated target instance.</returns>
    TTarget Migrate<TSource, TTarget>(TSource source);
}
