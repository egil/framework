<#
.SYNOPSIS
    Runs the BenchmarkDotNet scenarios pinned to one physical core and stores the
    reports under a label, so two runs can be compared with compare-perf.ps1.

.DESCRIPTION
    Absolute numbers on a busy desktop are not reproducible; relative before/after
    numbers are, provided both runs use the same cores, the same iteration budget
    and the boost clock does not swing with load on the other cores. This script
    fixes the first two. For the third, disable boost while measuring (needs an
    elevated shell; the setting survives until changed back):

        powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 0
        powercfg -setactive SCHEME_CURRENT

    and restore with PERFBOOSTMODE 2 afterwards.

    The benchmark config uses InProcessNoEmitToolchain, so the affinity mask pins
    the process that actually executes the benchmarks. The default mask is a
    high-numbered SMT pair (both logical threads of one physical core), so no
    other thread shares that core's pipeline; the scheduler fills low cores first.

.PARAMETER Label
    Name of the results folder under perf/<project>/BenchmarkDotNet.Artifacts/.

.PARAMETER Filter
    BenchmarkDotNet glob filter. Default runs everything.

.PARAMETER Affinity
    Decimal affinity mask passed to BenchmarkDotNet. Defaults to the last SMT pair.

.EXAMPLE
    ./scripts/run-perf.ps1 -Label baseline
    # ...apply changes...
    ./scripts/run-perf.ps1 -Label candidate
    ./scripts/compare-perf.ps1 -Baseline baseline -Candidate candidate
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Label,
    [string]$Filter = '*',
    [string]$Framework = 'net10.0',
    [int]$IterationCount = 15,
    [int]$WarmupCount = 3,
    [string]$Affinity
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$perfProject = Join-Path $root 'perf\Egil.SystemTextJson.Migration.PerfTests'
$artifacts = Join-Path $perfProject "BenchmarkDotNet.Artifacts\$Label"

if (-not $Affinity) {
    # Both logical threads of one physical core, assuming SMT pairs are adjacent (true for
    # Windows on AMD and Intel). BenchmarkDotNet parses --affinity as a signed 32-bit decimal
    # integer, so bit 31 is unusable and a 32-thread machine gets the pair below the top one.
    $shift = [Math]::Floor(([Math]::Min([Environment]::ProcessorCount, 31) - 2) / 2) * 2
    $Affinity = ([int]3 -shl $shift).ToString()
}

Write-Host "Label:      $Label"
Write-Host "Framework:  $Framework"
Write-Host "Affinity:   $Affinity"
Write-Host "Iterations: $IterationCount (warmup $WarmupCount)"
Write-Host "Artifacts:  $artifacts"

& dotnet run --project $perfProject -c Release --framework $Framework --no-build -- `
    --filter $Filter `
    --affinity $Affinity `
    --iterationCount $IterationCount `
    --warmupCount $WarmupCount `
    --exporters json github `
    --artifacts $artifacts

if ($LASTEXITCODE -ne 0) {
    throw "Benchmark run failed with exit code $LASTEXITCODE"
}
