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
    settings are changed here. BenchmarkDotNet does not touch processor boost,
    so the clock still swings unless it is disabled on the High Performance
    scheme; perf-compare-refs.ps1 does that for the run and restores it, this
    script does not.

    BenchmarkDotNet applies the affinity mask to the process that executes the benchmarks:
    this one for the in-process scenario config, the child process for the hot-path config
    on .NET 10. The default mask is one
    SMT pair (both logical threads of one physical core), so no other thread
    shares that core's pipeline: a P-core pair on hybrid Intel parts, otherwise a
    high-numbered pair because the scheduler fills low cores first.

.PARAMETER Label
    Name of the results folder under perf/<project>/BenchmarkDotNet.Artifacts/.

.PARAMETER Filter
    BenchmarkDotNet glob filter. Default runs everything.

.PARAMETER Affinity
    Decimal affinity mask passed to BenchmarkDotNet. Defaults to the last SMT pair.

.PARAMETER WarmupCount
    Fixed number of warmup iterations. By default each benchmark's job decides: the jobs warm
    up until iterations stop trending, because tiered compilation can take seconds to settle.

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
    [string]$Framework,
    [int]$IterationCount = 15,
    [int]$WarmupCount,
    [string]$Affinity
)

$ErrorActionPreference = 'Stop'
$warmupArgs = if ($PSBoundParameters.ContainsKey('WarmupCount')) { @('--warmupCount', $WarmupCount) } else { @() }
$warmupText = if ($warmupArgs) { "warmup $WarmupCount" } else { 'automatic warmup' }
$root = Split-Path $PSScriptRoot -Parent

# Same default as perf-compare-refs.ps1 and the README's perf tables: the net11.0 build once
# an 11.0.100-rc or later SDK is installed (its union scenario only exists there), else net10.0.
function Get-DefaultFramework {
    $sdks = & dotnet --list-sdks
    $hasRc = $sdks | Where-Object { $_ -match '^11\.0\.\d+(-rc|-rtm|\s)' -or $_ -match '^11\.0\.\d+ ' }
    if ($hasRc) { return 'net11.0' }
    return 'net10.0'
}

if (-not $Framework) { $Framework = Get-DefaultFramework }
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
Write-Host "Iterations: $IterationCount ($warmupText)"
Write-Host "Artifacts:  $artifacts"

# Build first so the run itself uses --no-build: a rebuild inside dotnet run would race a
# benchmark process still holding the previous output locked.
& dotnet build $perfProject -c Release --framework $Framework --nologo -v quiet
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

& dotnet run --project $perfProject -c Release --framework $Framework --no-build -- `
    --filter $Filter `
    --affinity $Affinity `
    --iterationCount $IterationCount `
    @warmupArgs `
    --exporters json github `
    --artifacts $artifacts

if ($LASTEXITCODE -ne 0) {
    throw "Benchmark run failed with exit code $LASTEXITCODE"
}
