namespace Egil.Orleans.StateMigration.Tests.SystemTextJson;

[CollectionDefinition(Name)]
public sealed class OrleansInProcessClusterCollection : ICollectionFixture<OrleansInProcessClusterFixture>
{
    public const string Name = "OrleansInProcessCluster";
}
