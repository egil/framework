<#
.SYNOPSIS
    Updates performance documentation from BenchmarkDotNet output.

.DESCRIPTION
    Reads BDN -report-github.md files from the perf project artifacts,
    copies full reports to docs/perf/, and updates the curated summary
    table in README.md.

.EXAMPLE
    ./scripts/update-perf-docs.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$artifactsDir = Join-Path $root 'perf\Egil.SystemTextJson.Migration.PerfTests\BenchmarkDotNet.Artifacts\results'
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
    $header = @"
# $Title

> Auto-generated from BenchmarkDotNet output by ``scripts/update-perf-docs.ps1``.
> Do not edit manually. Re-run benchmarks and this script to update.

"@
    $output = $header + $content
    $destPath = Join-Path $docsDir $DestName
    Set-Content -Path $destPath -Value $output -NoNewline
    Write-Host "Wrote $destPath"
}

Copy-BdnReport $sourceGenReport 'source-gen-benchmarks.md' 'Source-Generated Benchmarks'
Copy-BdnReport $reflectionReport 'reflection-benchmarks.md' 'Reflection Benchmarks'

# --- Parse source-gen report and build curated summary ---

$lines = Get-Content $sourceGenReport

# Parse table rows from BDN markdown output.
# BDN table format: | Method | Categories | TagCount | Mean | Error | StdDev | Ratio | RatioSD | Gen0 | Gen1 | Allocated | Alloc Ratio |
$dataRows = @()
foreach ($line in $lines) {
    if ($line -match '^\|\s+\*{0,2}(\w+)\*{0,2}\s+\|') {
        # Skip header/separator rows
        if ($line -match '^\| Method' -or $line -match '^\|[-:]') { continue }
        # Skip empty separator rows
        if ($line -match '^\|\s+\|') { continue }

        # Strip bold markers for parsing
        $clean = $line -replace '\*{2}', ''
        $cells = $clean -split '\|' | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }

        if ($cells.Count -ge 12) {
            $dataRows += [PSCustomObject]@{
                Method     = $cells[0]
                Categories = $cells[1]
                TagCount   = [int]$cells[2]
                Ratio      = $cells[6]
                AllocRatio = $cells[11]
            }
        }
    }
}

# Build curated summary: pick the JsonMigratable* rows
$scenarios = @(
    @{ Label = '**No migration (happy path)**'; Method = 'JsonMigratableNoMigration' }
    @{ Label = '**Static migration**';          Method = 'JsonMigratableStaticMigration' }
    @{ Label = '**External migration**';        Method = 'JsonMigratableExternalMigration' }
    @{ Label = '**Undiscriminated source migration**'; Method = 'JsonMigratableUndiscriminatedSourceMigration' }
    @{ Label = '**Legacy payload**';            Method = 'JsonMigratableLegacyPayload' }
    @{ Label = '**Serialization**';             Method = 'JsonMigratableSerializeNoMigration' }
)

$summaryLines = @()
$summaryLines += '| Scenario | TagCount | Ratio vs plain STJ | Alloc Ratio |'
$summaryLines += '|----------|:--------:|:-------------------:|:-----------:|'

foreach ($scenario in $scenarios) {
    $rows = $dataRows | Where-Object { $_.Method -eq $scenario.Method } | Sort-Object TagCount
    $first = $true
    foreach ($row in $rows) {
        $ratio = $row.Ratio
        # Format ratio as "X.XX×"
        if ($ratio -match '[\d.]+') {
            $ratioNum = [double]$ratio
            $ratioStr = '{0:F2}×' -f $ratioNum
        }
        else {
            $ratioStr = $ratio
        }

        $label = if ($first) { $scenario.Label } else { '' }
        $summaryLines += "| $label | $($row.TagCount) | $ratioStr | $($row.AllocRatio) |"
        $first = $false
    }
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
