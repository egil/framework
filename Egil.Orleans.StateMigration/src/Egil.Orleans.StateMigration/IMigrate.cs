namespace Egil.Orleans.StateMigration;

public interface IMigrate<in TSource, out TTarget>
{
    TTarget Migrate(TSource source);
}
