<#
.SYNOPSIS
    Benchmarks two git refs of the library against each other and writes a comparison.

.DESCRIPTION
    Checks both refs out into temporary git worktrees, builds the benchmark project in each,
    runs them pinned to one physical core in interleaved rounds (baseline, candidate,
    baseline, candidate, ...) and compares each round with compare-perf.ps1. Interleaving
    shows drift: a change that is real moves the same way in every round, noise does not.

    The candidate's benchmark project (perf/Egil.SystemTextJson.Migration.PerfTests) is
    copied over the baseline's before building, so both refs measure identical payloads
    and benchmark code; only the library differs. Scenarios the baseline library cannot
    run would fail its build, so keep benchmark changes compatible with the baseline
    library or pass a -BaselineRef that already contains them.

    Run it from any checkout of the repository on an otherwise idle machine:

        .\Egil.SystemTextJson.Migration\scripts\perf-compare-refs.ps1 -BaselineRef main -CandidateRef HEAD

    BenchmarkDotNet switches the active power scheme to High Performance for the run and
    restores it afterwards, so this script does not touch power settings. To freeze the
    clock, disable processor boost on the High Performance scheme by hand before running;
    the summary records the machine but not the boost state.

    Results land in <repo>\Egil.SystemTextJson.Migration\perf\Egil.SystemTextJson.Migration.PerfTests\BenchmarkDotNet.Artifacts\compare-<timestamp>\
    (git-ignored). summary.md there holds every per-round comparison plus the raw
    BenchmarkDotNet tables; send that file back.

.PARAMETER BaselineRef
    Git ref of the library to compare against, for example main or a commit SHA.

.PARAMETER CandidateRef
    Git ref of the library under test. Defaults to HEAD.

.PARAMETER Framework
    Target framework to benchmark. Defaults to net11.0 when an 11.0.100-rc or later SDK is
    installed, otherwise net10.0 (the net11.0 target needs the union keyword from RC1).

.PARAMETER Rounds
    How many baseline/candidate pairs to run. Two rounds take roughly an hour for the full
    scenario set on a desktop core; use -Filter to narrow.

.PARAMETER Filter
    BenchmarkDotNet glob filter, for example '*SourceGen*' or '*Serialize*'.

.PARAMETER Affinity
    Decimal affinity mask for the benchmark process. Defaults to one SMT pair: a P-core pair
    on hybrid Intel parts, otherwise a pair near the top of the core range (BenchmarkDotNet
    parses the mask as a signed 32-bit integer). The summary records the mask; check it
    against the machine's topology before trusting absolute numbers.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$BaselineRef,
    [string]$CandidateRef = 'HEAD',
    [string]$Framework,
    [int]$Rounds = 2,
    [string]$Filter = '*',
    [int]$IterationCount = 15,
    [int]$WarmupCount = 3,
    [string]$Affinity
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$repoRoot = (& git -C $projectRoot rev-parse --show-toplevel).Trim()
$perfRelative = 'Egil.SystemTextJson.Migration\perf\Egil.SystemTextJson.Migration.PerfTests'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outputDir = Join-Path $projectRoot "perf\Egil.SystemTextJson.Migration.PerfTests\BenchmarkDotNet.Artifacts\compare-$stamp"
$worktreeRoot = Join-Path ([IO.Path]::GetTempPath()) "stjm-perf-$stamp"

function Resolve-Sha([string]$ref) {
    $sha = (& git -C $repoRoot rev-parse --verify --quiet "$ref^{commit}").Trim()
    if (-not $sha) { throw "Cannot resolve git ref '$ref'" }
    return $sha
}

function Get-DefaultFramework {
    $sdks = & dotnet --list-sdks
    $hasRc = $sdks | Where-Object { $_ -match '^11\.0\.\d+(-rc|-rtm|\s)' -or $_ -match '^11\.0\.\d+ ' }
    if ($hasRc) { return 'net11.0' }
    return 'net10.0'
}

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

if (-not $Framework) { $Framework = Get-DefaultFramework }
if (-not $Affinity) { $Affinity = Get-DefaultAffinity }

$baselineSha = Resolve-Sha $BaselineRef
$candidateSha = Resolve-Sha $CandidateRef
if ($baselineSha -eq $candidateSha) { throw "Baseline and candidate resolve to the same commit $baselineSha" }

$refs = [ordered]@{
    baseline  = @{ Sha = $baselineSha; Ref = $BaselineRef; Dir = Join-Path $worktreeRoot 'baseline' }
    candidate = @{ Sha = $candidateSha; Ref = $CandidateRef; Dir = Join-Path $worktreeRoot 'candidate' }
}

New-Item -ItemType Directory -Force $outputDir | Out-Null
$machine = Get-CimInstance Win32_Processor | Select-Object -First 1
$summary = [System.Collections.Generic.List[string]]::new()
$summary.Add("# Benchmark comparison $stamp")
$summary.Add('')
$summary.Add("| | |")
$summary.Add("|---|---|")
$summary.Add("| Machine | $($machine.Name.Trim()), $([Environment]::ProcessorCount) logical CPUs, $([Environment]::OSVersion.VersionString) |")
$summary.Add("| Baseline | ``$BaselineRef`` = ``$baselineSha`` |")
$summary.Add("| Candidate | ``$CandidateRef`` = ``$candidateSha`` |")
$summary.Add("| Framework | $Framework |")
$summary.Add("| Affinity | $Affinity (0x$([Convert]::ToString([int]$Affinity, 16))) |")
$summary.Add("| Iterations | $IterationCount (warmup $WarmupCount), $Rounds round(s), filter ``$Filter`` |")
$summary.Add('')

try {
    foreach ($name in $refs.Keys) {
        $entry = $refs[$name]
        Write-Host "Creating worktree for $name ($($entry.Sha)) at $($entry.Dir)"
        & git -C $repoRoot worktree add --detach $entry.Dir $entry.Sha | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "git worktree add failed for $name" }
    }

    # Same benchmark code for both refs: copy the candidate's perf project sources over the
    # baseline's. Only source files are copied so nothing from a previous build leaks in.
    $candidatePerf = Join-Path $refs.candidate.Dir $perfRelative
    $baselinePerf = Join-Path $refs.baseline.Dir $perfRelative
    Get-ChildItem $baselinePerf -File | Remove-Item -Force
    Get-ChildItem $candidatePerf -File | Copy-Item -Destination $baselinePerf -Force

    foreach ($name in $refs.Keys) {
        $perfDir = Join-Path $refs[$name].Dir $perfRelative
        Write-Host "Building $name ($Framework)"
        & dotnet build $perfDir -c Release --framework $Framework --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw "Build failed for $name" }
    }

    for ($round = 1; $round -le $Rounds; $round++) {
        foreach ($name in $refs.Keys) {
            $label = "$name-r$round"
            $perfDir = Join-Path $refs[$name].Dir $perfRelative
            $artifacts = Join-Path $outputDir $label
            Write-Host "Round $round`: running $name -> $artifacts"
            & dotnet run --project $perfDir -c Release --framework $Framework --no-build -- `
                --filter $Filter `
                --affinity $Affinity `
                --iterationCount $IterationCount `
                --warmupCount $WarmupCount `
                --exporters json github `
                --artifacts $artifacts
            if ($LASTEXITCODE -ne 0) { throw "Benchmark run failed for $label" }
            if (-not (Test-Path (Join-Path $artifacts 'results'))) { throw "No results produced for $label; check the BenchmarkDotNet output above" }
        }

        $summary.Add("## Round $round`: baseline vs candidate")
        $summary.Add('')
        $summary.Add('```')
        $comparison = & (Join-Path $PSScriptRoot 'compare-perf.ps1') -Baseline "baseline-r$round" -Candidate "candidate-r$round" -ArtifactsDir $outputDir | Out-String -Width 220
        $summary.Add($comparison.TrimEnd())
        $summary.Add('```')
        $summary.Add('')
    }

    if ($Rounds -ge 2) {
        $summary.Add('## Drift check: baseline round 1 vs baseline round 2')
        $summary.Add('')
        $summary.Add('Differences here are machine noise, not code; treat candidate deltas of the same size as noise too.')
        $summary.Add('')
        $summary.Add('```')
        $drift = & (Join-Path $PSScriptRoot 'compare-perf.ps1') -Baseline 'baseline-r1' -Candidate 'baseline-r2' -ArtifactsDir $outputDir | Out-String -Width 220
        $summary.Add($drift.TrimEnd())
        $summary.Add('```')
        $summary.Add('')
    }

    foreach ($report in Get-ChildItem $outputDir -Recurse -Filter '*-report-github.md') {
        $label = $report.Directory.Parent.Name
        $summary.Add("## Raw report: $label / $($report.BaseName -replace '-report-github$', '' -replace '^Egil\.SystemTextJson\.Migration\.PerfTests\.', '')")
        $summary.Add('')
        $summary.Add((Get-Content $report.FullName -Raw).TrimEnd())
        $summary.Add('')
    }

    $summaryPath = Join-Path $outputDir 'summary.md'
    Set-Content -Path $summaryPath -Value ($summary -join "`n") -NoNewline
    Write-Host ''
    Write-Host "Summary written to $summaryPath"
}
finally {
    foreach ($name in $refs.Keys) {
        if (Test-Path $refs[$name].Dir) {
            & git -C $repoRoot worktree remove --force $refs[$name].Dir | Out-Null
        }
    }

    if (Test-Path $worktreeRoot) { Remove-Item -Recurse -Force $worktreeRoot -ErrorAction SilentlyContinue }
}
