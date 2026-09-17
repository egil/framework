; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
STP001 | Egil.StronglyTypedPrimitives | Warning | JsonSerializerContext cannot see the generated JsonConverter attribute
STP004 | Egil.StronglyTypedPrimitives | Warning | System.Text.Json support for strongly typed primitives requires net8.0 or later
