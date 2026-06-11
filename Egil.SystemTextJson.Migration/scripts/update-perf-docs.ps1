<#
.SYNOPSIS
    Updates performance documentation from BenchmarkDotNet output.

.DESCRIPTION
    Reads BDN -report-github.md files from BenchmarkDotNet artifacts,
    copies full reports to docs/perf/, and updates the source-generated
    benchmark table in README.md.

.EXAMPLE
    dotnet run --project perf/Egil.SystemTextJson.Migration.PerfTests -c Release
    ./scripts/update-perf-docs.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$repoRoot = Split-Path $root -Parent

$artifactDirCandidates = @(
    (Join-Path $root 'perf\Egil.SystemTextJson.Migration.PerfTests\BenchmarkDotNet.Artifacts\results'),
    (Join-Path $root 'BenchmarkDotNet.Artifacts\results'),
    (Join-Path $repoRoot 'BenchmarkDotNet.Artifacts\results')
)
$artifactsDir = $artifactDirCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $artifactsDir) {
    Write-Error "Benchmark reports not found. Run benchmarks first: dotnet run --project perf\Egil.SystemTextJson.Migration.PerfTests -c Release"
}

$docsDir = Join-Path $root 'docs\perf'
$readme = Join-Path $root 'README.md'

$sourceGenReport = Join-Path $artifactsDir 'Egil.SystemTextJson.Migration.PerfTests.SourceGenMigrationScenarioBenchmarks-report-github.md'
$reflectionReport = Join-Path $artifactsDir 'Egil.SystemTextJson.Migration.PerfTests.ReflectionMigrationScenarioBenchmarks-report-github.md'

if (-not (Test-Path $sourceGenReport)) {
    Write-Error "Source-gen benchmark report not found at: $sourceGenReport`nRun benchmarks first: dotnet run --project perf/Egil.SystemTextJson.Migration.PerfTests -c Release"
}
if (-not (Test-Path $reflectionReport)) {
    Write-Error "Reflection benchmark report not found at: $reflectionReport`nRun benchmarks first: dotnet run --project perf/Egil.SystemTextJson.Migration.PerfTests -c Release"
}

# --- Copy full reports to docs/perf/ ---

function Copy-BdnReport {
    param([string]$Source, [string]$DestName, [string]$Title)

    $content = Get-Content $Source -Raw
    $content = [regex]::Replace($content, '(?m)^\|\s+\*{0,2}PolymorphicPlainStj[^\r\n]*(\r?\n)?', '')

    $header = @"
# $Title

> Auto-generated from BenchmarkDotNet output by ``scripts/update-perf-docs.ps1``.
> Do not edit manually. Re-run benchmarks and this script to update.
> Public reports omit the internal ``PolymorphicPlainStj*`` guardrail benchmarks.

"@
    $output = $header + $content
    $destPath = Join-Path $docsDir $DestName
    Set-Content -Path $destPath -Value $output -NoNewline
    Write-Host "Wrote $destPath"
}

Copy-BdnReport $sourceGenReport 'source-gen-benchmarks.md' 'Source-Generated Benchmarks'
Copy-BdnReport $reflectionReport 'reflection-benchmarks.md' 'Reflection Benchmarks'

# --- Parse source-gen report and build README table ---

$lines = Get-Content $sourceGenReport

# Parse table rows from BDN markdown output.
# BDN table format can vary when extra GC columns appear, so locate values by column name.
$dataRows = @()
$columnIndexes = $null
foreach ($line in $lines) {
    if ($line -match '^\|\s*Method\s*\|') {
        $headerCells = $line -split '\|' | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }
        $columnIndexes = @{}
        for ($index = 0; $index -lt $headerCells.Count; $index++) {
            $columnIndexes[$headerCells[$index]] = $index
        }

        continue
    }

    if ($line -match '^\|\s+\*{0,2}(\w+)\*{0,2}\s+\|') {
        # Skip separator rows
        if ($line -match '^\|[-:]') { continue }
        # Skip empty separator rows
        if ($line -match '^\|\s+\|') { continue }
        if (-not $columnIndexes) { continue }

        # Strip bold markers for parsing
        $clean = $line -replace '\*{2}', ''
        $cells = $clean -split '\|' | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }

        $requiredColumns = @('Method', 'Categories', 'PayloadSize', 'Mean', 'Ratio', 'RatioSD', 'Allocated', 'Alloc Ratio')
        $hasRequiredColumns = $true
        foreach ($column in $requiredColumns) {
            if (-not $columnIndexes.ContainsKey($column) -or $cells.Count -le $columnIndexes[$column]) {
                $hasRequiredColumns = $false
                break
            }
        }

        if ($hasRequiredColumns) {
            $dataRows += [PSCustomObject]@{
                Method      = $cells[$columnIndexes['Method']]
                Categories  = $cells[$columnIndexes['Categories']]
                PayloadSize = $cells[$columnIndexes['PayloadSize']]
                Mean        = $cells[$columnIndexes['Mean']]
                Ratio       = $cells[$columnIndexes['Ratio']]
                RatioSD     = $cells[$columnIndexes['RatioSD']]
                Allocated   = $cells[$columnIndexes['Allocated']]
                AllocRatio  = $cells[$columnIndexes['Alloc Ratio']]
            }
        }
    }
}

function Get-ScenarioSortKey {
    param([string]$Categories)

    switch ($Categories) {
        'Deserialize,NoMigration' { return 0 }
        'Deserialize,StaticMigration' { return 1 }
        'Deserialize,ExternalMigration' { return 2 }
        'Deserialize,UndiscriminatedSourceMigration' { return 3 }
        'Deserialize,LegacyPayload' { return 4 }
        'Serialize' { return 5 }
        default { return [int]::MaxValue }
    }
}

function Get-PayloadSizeSortKey {
    param([string]$PayloadSize)

    switch ($PayloadSize) {
        'Small' { return 0 }
        'Medium' { return 1 }
        'Large' { return 2 }
        default { return [int]::MaxValue }
    }
}

function Get-MethodSortKey {
    param([string]$Method)

    if ($Method -like 'PlainStj*') {
        return 0
    }

    if ($Method -like 'JsonMigratable*') {
        return 1
    }

    return [int]::MaxValue
}

function Get-ScenarioLabel {
    param([string]$Categories)

    switch ($Categories) {
        'Deserialize,NoMigration' { return '**No migration (happy path)**' }
        'Deserialize,StaticMigration' { return '**Static migration**' }
        'Deserialize,ExternalMigration' { return '**External migration**' }
        'Deserialize,UndiscriminatedSourceMigration' { return '**Undiscriminated source migration**' }
        'Deserialize,LegacyPayload' { return '**Legacy payload**' }
        'Serialize' { return '**Serialization**' }
        default { return $Categories }
    }
}

function Get-MethodLabel {
    param([string]$Method)

    switch ($Method) {
        'PlainStjNoMigration' { return 'Plain STJ' }
        'PlainStjSerialize' { return 'Plain STJ' }
        'JsonMigratableNoMigration' { return 'JsonMigratable' }
        'JsonMigratableSerialize' { return 'JsonMigratable' }
        'PlainStjStaticMigrationManual' { return 'Manual STJ migration' }
        'PlainStjExternalMigrationManual' { return 'Manual STJ migration' }
        'PlainStjUndiscriminatedSourceMigrationManual' { return 'Manual STJ migration' }
        'PlainStjLegacyPayloadManual' { return 'Plain STJ + tracking' }
        'JsonMigratableStaticMigration' { return 'JsonMigratable' }
        'JsonMigratableExternalMigration' { return 'JsonMigratable' }
        'JsonMigratableUndiscriminatedSourceMigration' { return 'JsonMigratable' }
        'JsonMigratableLegacyPayload' { return 'JsonMigratable' }
        default { return $Method }
    }
}

$publicRows = $dataRows |
    Where-Object { $_.Method -notlike 'PolymorphicPlainStj*' } |
    Sort-Object `
        @{ Expression = { Get-ScenarioSortKey $_.Categories } }, `
        @{ Expression = { Get-PayloadSizeSortKey $_.PayloadSize } }, `
        @{ Expression = { Get-MethodSortKey $_.Method } }

$summaryLines = @()
$summaryLines += '| Scenario | Method | Payload size | Mean | Ratio | RatioSD | Allocated | Alloc Ratio |'
$summaryLines += '|----------|--------|:------------:|-----:|------:|--------:|----------:|------------:|'

$previousScenario = $null
foreach ($row in $publicRows) {
    $scenarioLabel = if ($row.Categories -ne $previousScenario) { Get-ScenarioLabel $row.Categories } else { '' }
    $summaryLines += "| $scenarioLabel | $(Get-MethodLabel $row.Method) | $($row.PayloadSize) | $($row.Mean) | $($row.Ratio) | $($row.RatioSD) | $($row.Allocated) | $($row.AllocRatio) |"
    $previousScenario = $row.Categories
}

$summaryTable = $summaryLines -join "`n"

# --- Update README.md summary table ---

$readmeContent = Get-Content $readme -Raw

$startMarker = '<!-- perf-summary:start -->'
$endMarker = '<!-- perf-summary:end -->'

if ($readmeContent -match [regex]::Escape($startMarker) -and $readmeContent -match [regex]::Escape($endMarker)) {
    $pattern = "(?s)$([regex]::Escape($startMarker)).*?$([regex]::Escape($endMarker))"
    $replacement = "$startMarker`n$summaryTable`n$endMarker"
    $readmeContent = [regex]::Replace($readmeContent, $pattern, $replacement)
    Set-Content -Path $readme -Value $readmeContent -NoNewline
    Write-Host "Updated README.md performance summary table"
}
else {
    Write-Warning "Could not find perf-summary markers in README.md. Add '$startMarker' and '$endMarker' around the summary table."
}

Write-Host "`nDone! Review changes with: git diff -- docs/perf/ README.md"
