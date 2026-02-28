namespace Egil.SystemTextJson.Migration;

/// <summary>
/// Defines a static migration contract from a source type to a target type.
/// </summary>
/// <typeparam name="TSource">The source type.</typeparam>
/// <typeparam name="TTarget">The target type.</typeparam>
public interface IMigrateFrom<TSource, TTarget>
    where TTarget : IMigrateFrom<TSource, TTarget>
{
    /// <summary>
    /// Attempts to migrate <paramref name="source"/> into <typeparamref name="TTarget"/>.
    /// </summary>
    /// <param name="source">The source instance.</param>
    /// <param name="result">The migrated target instance.</param>
    /// <returns><see langword="true"/> when migration succeeded.</returns>
    static abstract bool TryMigrateFrom(TSource source, out TTarget result);
}
