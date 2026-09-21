<#
.SYNOPSIS
    Runs the BenchmarkDotNet scenarios pinned to one physical core and stores the
    reports under a label, so two runs can be compared with compare-perf.ps1.

.DESCRIPTION
    Absolute numbers on a busy desktop are not reproducible; relative before/after
    numbers are, provided both runs use the same cores, the same iteration budget
    and the boost clock does not swing with load on the other cores. This script
    fixes the first two. BenchmarkDotNet itself switches the active power scheme
    to High Performance for the run and restores it afterwards, so no power
    settings are changed here; to freeze the clock, disable processor boost on
    the High Performance scheme by hand before measuring.

    The benchmark config uses InProcessNoEmitToolchain, so the affinity mask pins
    the process that actually executes the benchmarks. The default mask is one
    SMT pair (both logical threads of one physical core), so no other thread
    shares that core's pipeline: a P-core pair on hybrid Intel parts, otherwise a
    high-numbered pair because the scheduler fills low cores first.

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

function Get-DefaultAffinity {
    # Both logical threads of one physical core. BenchmarkDotNet parses the mask as a signed
    # 32-bit integer, so bit 31 is unusable. On a hybrid Intel part (P-cores with SMT first,
    # E-cores without SMT last; core count times two is not the thread count) the top logical
    # CPUs are E-cores, so the second P-core pair is used there; elsewhere a high pair, which
    # the scheduler fills last on a busy machine.
    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    $logical = [Environment]::ProcessorCount
    if ($cpu.NumberOfCores * 2 -ne $logical) {
        return ([int]3 -shl 2).ToString()
    }

    $shift = [Math]::Floor(([Math]::Min($logical, 31) - 2) / 2) * 2
    return ([int]3 -shl $shift).ToString()
}

if (-not $Affinity) {
    $Affinity = Get-DefaultAffinity
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
