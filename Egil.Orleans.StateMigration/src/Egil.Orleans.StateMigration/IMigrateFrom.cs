namespace Egil.Orleans.StateMigration;

public interface IMigrateFrom<in TSource, TTarget>
{
    static abstract TTarget From(TSource source);
}
