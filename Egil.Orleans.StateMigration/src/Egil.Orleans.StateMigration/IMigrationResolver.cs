namespace Egil.Orleans.StateMigration;

public interface IMigrationResolver
{
    TTarget Migrate<TSource, TTarget>(TSource source);
}
