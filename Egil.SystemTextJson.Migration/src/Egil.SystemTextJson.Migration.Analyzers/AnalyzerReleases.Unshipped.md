; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
STJM0002 | Egil.SystemTextJson.Migration | Warning | [JsonMigratable] targets must implement IMigrateFrom instead of IMigrate.
STJM0010 | Egil.SystemTextJson.Migration | Warning | Legacy payload type used outside migration
