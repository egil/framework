namespace Egil.SystemTextJson.Migration.Migrations;

internal static class StaticMigratorContracts
{
    /// <summary>
    /// Enumerates the source types of every <see cref="IMigrateFrom{TSource, TTarget}"/> contract
    /// implemented by <paramref name="targetType"/>.
    /// </summary>
    public static IEnumerable<Type> GetSourceTypes(Type targetType)
    {
        foreach (Type @interface in targetType.GetInterfaces())
        {
            if (!@interface.IsGenericType
                || @interface.ContainsGenericParameters
                || @interface.GetGenericTypeDefinition() != typeof(IMigrateFrom<,>))
            {
                continue;
            }

            yield return @interface.GetGenericArguments()[0];
        }
    }
}
