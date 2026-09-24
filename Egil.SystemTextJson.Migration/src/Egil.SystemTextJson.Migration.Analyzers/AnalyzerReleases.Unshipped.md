; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
STJM0002 | Egil.SystemTextJson.Migration | Warning | [JsonMigratable] targets must implement IMigrateFrom instead of IMigrate.
STJM0004 | Egil.SystemTextJson.Migration | Warning | Migratable targets must serialize as JSON objects.
STJM0007 | Egil.SystemTextJson.Migration | Warning | JSON context lacks metadata for migration discriminator strings.
STJM0010 | Egil.SystemTextJson.Migration | Warning | Legacy payload type used outside migration
STJM0011 | Egil.SystemTextJson.Migration | Warning | Legacy payload type has no visible migration source contract.
STJM0003 | Egil.SystemTextJson.Migration | Warning | Undiscriminated source type has no matching migrator contract.
STJM0005 | Egil.SystemTextJson.Migration | Warning | JsonMigratable conflicts with System.Text.Json polymorphism in the same hierarchy.
STJM0001 | Egil.SystemTextJson.Migration | Warning | Duplicate migration source discriminator for the same target.
