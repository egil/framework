namespace Egil.SystemTextJson.Migration
{
    public interface IMigrate<TSource, TTarget>
    {
    }

    public interface IMigrateFrom<TSource, TTarget>
    {
    }

    public sealed class JsonMigratableAttribute : Attribute
    {
    }
}

namespace Net11UnionFixture
{
    [Egil.SystemTextJson.Migration.JsonMigratable]
    public sealed class MigratableCase
    {
    }

    public sealed class OtherCase
    {
    }

    public union SupportedUnion(MigratableCase, OtherCase);
}
