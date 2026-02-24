namespace Egil.Orleans.StateMigration;

/// <summary>
/// Defines a static, type-owned migration from <typeparamref name="TSource"/> to
/// <typeparamref name="TTarget"/>.
/// </summary>
/// <typeparam name="TSource">The historical source state type.</typeparam>
/// <typeparam name="TTarget">The current target state type.</typeparam>
/// <remarks>
/// This is the preferred migration mechanism. When both static and external migrators exist for the same
/// source/target pair, the static migration on the target type is selected first.
/// </remarks>
/// <example>
/// <code><![CDATA[
/// public sealed class CartStateV2 : IMigrateFrom<CartStateV1, CartStateV2>
/// {
///     public static CartStateV2 From(CartStateV1 source) => new();
/// }
/// ]]></code>
/// </example>
public interface IMigrateFrom<in TSource, TTarget>
{
    /// <summary>
    /// Creates a target instance from a source instance.
    /// </summary>
    /// <param name="source">The source state to migrate from.</param>
    /// <returns>The migrated target state.</returns>
    /// <remarks>
    /// The method is static so migration logic can live with the target type without requiring DI.
    /// </remarks>
    static abstract TTarget From(TSource source);
}
