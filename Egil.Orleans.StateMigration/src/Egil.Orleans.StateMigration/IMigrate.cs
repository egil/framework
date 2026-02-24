namespace Egil.Orleans.StateMigration;

/// <summary>
/// Defines an external migrator from <typeparamref name="TSource"/> to <typeparamref name="TTarget"/>.
/// </summary>
/// <typeparam name="TSource">The historical source state type.</typeparam>
/// <typeparam name="TTarget">The current target state type.</typeparam>
/// <remarks>
/// This contract is used when the target type does not provide a static migration via
/// <see cref="IMigrateFrom{TSource, TTarget}"/>. Implementations are expected to be stateless and safe
/// for singleton lifetime registration in dependency injection.
/// </remarks>
/// <example>
/// <code><![CDATA[
/// services.AddSingleton<IMigrate<StateV1, StateV2>, StateV1ToStateV2Migrator>();
/// ]]></code>
/// </example>
public interface IMigrate<in TSource, out TTarget>
{
    /// <summary>
    /// Migrates a source state instance to the current target shape.
    /// </summary>
    /// <param name="source">The source state to migrate from.</param>
    /// <returns>The migrated target state.</returns>
    /// <remarks>
    /// Implementations should be deterministic and should not mutate <paramref name="source"/>.
    /// </remarks>
    TTarget Migrate(TSource source);
}
