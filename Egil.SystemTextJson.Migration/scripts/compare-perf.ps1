<#
.SYNOPSIS
    Compares two labelled benchmark runs produced by run-perf.ps1.

.DESCRIPTION
    Reads the BenchmarkDotNet JSON reports of both labels, matches benchmarks by
    class, method and parameters, and prints mean, allocation and the relative
    change. A change is marked significant when the two means differ by more than
    three standard deviations of the noisier run, which is a coarse filter against
    desktop noise rather than a statistical test; look at the raw numbers for
    anything close to the line.

.PARAMETER ArtifactsDir
    Folder holding the labelled result folders. Defaults to the perf project's
    BenchmarkDotNet.Artifacts, where run-perf.ps1 writes.

.EXAMPLE
    ./scripts/compare-perf.ps1 -Baseline baseline -Candidate candidate
    ./scripts/compare-perf.ps1 -Baseline baseline -Candidate candidate -Match 'SourceGen*Serialize*'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Baseline,
    [Parameter(Mandatory)]
    [string]$Candidate,
    [string]$Match = '*',
    [string]$ArtifactsDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = if ($ArtifactsDir) { $ArtifactsDir } else { Join-Path $root 'perf\Egil.SystemTextJson.Migration.PerfTests\BenchmarkDotNet.Artifacts' }

function Read-Run {
    param([string]$Label)

    $dir = Join-Path $artifacts "$Label\results"
    if (-not (Test-Path $dir)) {
        throw "No results for label '$Label' at $dir"
    }

    $rows = @{}
    foreach ($file in Get-ChildItem $dir -Filter '*-report-full-compressed.json') {
        $report = Get-Content $file.FullName -Raw | ConvertFrom-Json
        foreach ($benchmark in $report.Benchmarks) {
            # Type is the short class name; FullName carries the parameters.
            $key = "$($benchmark.Type).$($benchmark.Method) [$($benchmark.Parameters)]"
            $rows[$key] = [PSCustomObject]@{
                Mean   = [double]$benchmark.Statistics.Mean
                StdDev = [double]$benchmark.Statistics.StandardDeviation
                Alloc  = if ($null -ne $benchmark.Memory) { [double]$benchmark.Memory.BytesAllocatedPerOperation } else { [double]::NaN }
            }
        }
    }

    return $rows
}

$before = Read-Run $Baseline
$after = Read-Run $Candidate

$table = foreach ($key in ($before.Keys + $after.Keys | Sort-Object -Unique)) {
    if ($key -notlike $Match) { continue }

    $b = $before[$key]
    $a = $after[$key]
    if ($null -eq $b -or $null -eq $a) {
        # No null-conditional operator here so the script also runs on Windows PowerShell 5.1.
        $baseMean = if ($b) { $b.Mean } else { $null }
        $candMean = if ($a) { $a.Mean } else { $null }
        $baseAlloc = if ($b) { $b.Alloc } else { $null }
        $candAlloc = if ($a) { $a.Alloc } else { $null }
        [PSCustomObject]@{ Benchmark = $key; 'Base ns' = $baseMean; 'Cand ns' = $candMean; 'Delta %' = $null; 'Base B' = $baseAlloc; 'Cand B' = $candAlloc; Flag = 'missing' }
        continue
    }

    $delta = ($a.Mean - $b.Mean) / $b.Mean * 100
    $noise = [Math]::Max($b.StdDev, $a.StdDev) * 3
    $flag = if ([Math]::Abs($a.Mean - $b.Mean) -gt $noise) { if ($delta -lt 0) { 'faster' } else { 'slower' } } else { '' }

    [PSCustomObject]@{
        Benchmark = $key
        'Base ns' = [Math]::Round($b.Mean, 1)
        'Cand ns' = [Math]::Round($a.Mean, 1)
        'Delta %' = [Math]::Round($delta, 1)
        'Base B'  = $b.Alloc
        'Cand B'  = $a.Alloc
        Flag      = $flag
    }
}

$table | Format-Table -AutoSize | Out-String -Width 220 | Write-Output
