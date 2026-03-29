namespace Egil.SystemTextJson.Migration.Migrations;

internal sealed record ExternalMigratorRegistration(
    Type SourceType,
    Type TargetType,
    TypeMetadata SourceMetadata,
    IMigratorInvoker Invoker);
