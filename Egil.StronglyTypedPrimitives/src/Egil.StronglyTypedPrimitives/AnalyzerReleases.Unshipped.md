; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
STP001 | Egil.StronglyTypedPrimitives | Warning | JsonSerializerContext cannot see the generated JsonConverter attribute
STP002 | Egil.StronglyTypedPrimitives | Warning | Validation attributes are ignored when IsValueValid is declared
STP003 | Egil.StronglyTypedPrimitives | Warning | Async validation attributes are not part of the value invariant
STP004 | Egil.StronglyTypedPrimitives | Warning | System.Text.Json support for strongly typed primitives requires net8.0 or later
