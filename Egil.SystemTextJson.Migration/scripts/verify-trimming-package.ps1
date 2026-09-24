<#
.SYNOPSIS
Verifies trimming/AOT diagnostics from a packed migration library on both supported frameworks.
.DESCRIPTION
Uses isolated consumers and package caches, retains logs and a package-hash results manifest,
and executes only the supported untrimmed consumers. -Publish adds rooted trim and NativeAOT
publish analysis and requires the platform's NativeAOT prerequisites.
.PARAMETER NativeCompileOnly
With -Publish, run the NativeAOT compiler but omit native linking. Intended for local Windows
machines without the C++ workload. This is partial publish evidence; CI must omit this switch.
.EXAMPLE
./verify-trimming-package.ps1 -PackagePath ../artifacts/package.nupkg -Publish
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$PackagePath,
    [string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/trimming'),
    [string]$RuntimeIdentifier = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier,
    [switch]$Publish,
    [switch]$NativeCompileOnly,
    [ValidateSet('net10.0', 'net11.0')] [string[]]$TargetFrameworks = @('net10.0', 'net11.0')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($NativeCompileOnly -and -not $Publish) { throw '-NativeCompileOnly requires -Publish.' }
$package = Get-Item -LiteralPath $PackagePath
if ($package.Extension -ne '.nupkg' -or $package.BaseName -notmatch '^Egil\.SystemTextJson\.Migration\.(?<version>\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)$') {
    throw "Unexpected package file name: $($package.Name)"
}
$version = $Matches.version
$run = Join-Path ([System.IO.Path]::GetFullPath($EvidenceDirectory)) ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
Write-Host "Diagnostic evidence: $run"
$evidence = [ordered]@{
    Package = $package.FullName
    PackageSha256 = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash
    Sdk = (& dotnet --version | Out-String).Trim()
    RuntimeIdentifier = $RuntimeIdentifier
    Publish = $Publish.IsPresent
    NativeCompileOnly = $NativeCompileOnly.IsPresent
    Results = @()
}
$evidence | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $run 'results.json')

function Invoke-DotNet([string[]]$Arguments, [string]$Log) {
    $output = & dotnet @Arguments 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    $output | Set-Content -LiteralPath $Log
    if ($exitCode -ne 0) { throw "dotnet $($Arguments[0]) failed ($exitCode). See $Log" }
    return $output
}

function Assert-Diagnostics([string]$Output, [string]$Mode, [string[]]$Sites, [hashtable]$Locations, [string]$Stage = 'build') {
    if ($Mode -eq 'plain') {
        if ($Output -match '\b(?:warning|error) [A-Z]+\d+') { throw 'The untrimmed consumer was not warning-free.' }
        return
    }
    $ids = if ($Mode -eq 'aot') { @('IL2026', 'IL3050') } else { @('IL2026') }
    foreach ($site in $Sites) {
        foreach ($id in $ids) {
            # Compiler format: Program.cs(19,9): warning IL2026: ...
            # Match each diagnostic line independently so another call's message cannot satisfy it.
            $location = $locations[$site]
            # NativeAOT reports a line without a column; Roslyn and ILLink include both.
            $line = @($Output -split '\r?\n' | Where-Object { $_ -match ('Program\.cs\(' + $location + '(?:,\d+)?\):.*\b' + $id + '\b') })
            if ($line.Count -eq 0) { throw "Missing $id for $site at Program.cs:$location." }
            foreach ($message in $line) {
                if ($message -notmatch 'PublishAot=false' -or $message -notmatch 'https://github.com/egil/framework/') {
                    throw "Missing corrective action or context URL: $message"
                }
                if ($id -eq 'IL2026' -and ($message -notmatch 'trimming may remove' -or $message -notmatch 'PublishTrimmed=false' -or $message -notmatch 'source-generated JsonSerializerContext')) {
                    throw "Missing trimming/source-generation guidance: $message"
                }
                if ($id -eq 'IL3050' -and $message -notmatch 'generic converters and invokers at runtime') {
                    throw "Missing dynamic-code explanation: $message"
                }
            }
        }
    }
    if ($Mode -eq 'trim' -and $Output -match '\bIL3050\b') { throw 'Trim-only analysis unexpectedly enabled AOT diagnostics.' }
    $warnings = @($Output -split '\r?\n' | Where-Object { $_ -match '\bwarning [A-Z]+\d+' })
    $allowedLines = @($Sites | ForEach-Object { $locations[$_] })
    if ($Sites -contains 'Classifier') {
        $allowedLines += $locations['UnionAttribute']
        if ($Stage -eq 'build' -and $Output -notmatch ('Program\.cs\(' + $locations['UnionAttribute'] + ',\d+\):.*\bIL2026\b')) {
            throw 'Missing IL2026 on the consumer JsonUnion attribute.'
        }
        # Roslyn reports the attribute's preservation requirement at the user's declaration.
        # Publish analysis also visits the generated constructor call and the attribute metadata.
        if ($Stage -ne 'build') {
            foreach ($id in $ids) {
                if ($Output -notmatch ('Context\.Result\.g\.cs\(\d+(?:,\d+)?\):.*\b' + $id + '\b')) {
                    throw "Missing $id for the source-generated union classifier activation."
                }
            }
        }
    }
    $locationPattern = 'Program\.cs\((' + ($allowedLines -join '|') + ')(?:,\d+)?\):'
    $unexpected = @($warnings | Where-Object {
        $isRequirement = $_ -match '\b(IL2026|IL3050)\b'
        $knownLocation = $_ -match $locationPattern
        $unionActivation = $Sites -contains 'Classifier' -and $_ -match 'JsonMigratableUnionTypeClassifier' -and
            ($_ -match 'Context\.Result\.g\.cs\(' -or $_ -match '(?:ILLink|ILC) : (?:Trim|AOT) analysis warning IL(?:2026|3050): Result:')
        $hasGuidance = $_ -match 'PublishAot=false' -and $_ -match 'source-generated JsonSerializerContext' -and
            $_ -match 'https://github.com/egil/framework/'
        -not ($isRequirement -and ($knownLocation -or $unionActivation) -and $hasGuidance)
    })
    if ($unexpected.Count) { throw "Unexpected diagnostics: $($unexpected -join [Environment]::NewLine)" }
}

$source = Get-Content (Join-Path $PSScriptRoot 'trimming-consumer/Program.cs') -Raw

# Keep real source locations (also embedded in publish PDBs), avoiding synthetic #line files.
$locations = @{}
$sourceLines = $source -split '\r?\n'
# Each '// diagnostic: <Site>' marker names the call on the line directly below it.
for ($index = 0; $index -lt $sourceLines.Length; $index++) {
    if ($sourceLines[$index] -match '// diagnostic: (.+)') { $locations[$Matches[1]] = $index + 2 }
}

foreach ($framework in $TargetFrameworks) {
    foreach ($mode in @('plain', 'trim', 'aot')) {
        $directory = Join-Path $run "$framework-$mode"
        New-Item -ItemType Directory -Path $directory | Out-Null
        # Do not inherit repository warnings, package references, or central version management.
        '<Project />' | Set-Content (Join-Path $directory 'Directory.Build.props')
        '<Project />' | Set-Content (Join-Path $directory 'Directory.Build.targets')
        '<Project />' | Set-Content (Join-Path $directory 'Directory.Packages.props')
        @"
<configuration><packageSources><clear />
<add key="package" value="$([System.Security.SecurityElement]::Escape($package.Directory.FullName))" />
<add key="nuget" value="https://api.nuget.org/v3/index.json" />
</packageSources></configuration>
"@ | Set-Content (Join-Path $directory 'NuGet.Config')
        $trim = ($mode -ne 'plain').ToString().ToLowerInvariant()
        $aot = ($mode -eq 'aot').ToString().ToLowerInvariant()
        @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$framework</TargetFramework>
    <Nullable>enable</Nullable>
    <EnableTrimAnalyzer>$trim</EnableTrimAnalyzer>
    <EnableAotAnalyzer>$aot</EnableAotAnalyzer>
    <PublishTrimmed>$trim</PublishTrimmed>
    <PublishAot>$aot</PublishAot>
    <TrimmerSingleWarn>false</TrimmerSingleWarn>
    <RestorePackagesPath>$([System.Security.SecurityElement]::Escape((Join-Path $run 'packages')))</RestorePackagesPath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Egil.SystemTextJson.Migration" Version="$version" />
    <TrimmerRootAssembly Include="Egil.SystemTextJson.Migration" Condition="'`$(PublishTrimmed)' == 'true'" />
  </ItemGroup>
</Project>
"@ | Set-Content (Join-Path $directory 'Consumer.csproj')
        $source | Set-Content (Join-Path $directory 'Program.cs')
        $project = Join-Path $directory 'Consumer.csproj'
        $null = Invoke-DotNet @('restore', $project, '--configfile', (Join-Path $directory 'NuGet.Config'), '-r', $RuntimeIdentifier) (Join-Path $directory 'restore.log')
        $output = Invoke-DotNet @('build', $project, '-c', 'Release', '--no-restore', '-r', $RuntimeIdentifier) (Join-Path $directory 'build.log')
        $sites = @('Setup', 'Provider', 'Generated', 'GeneratedProvider', 'RegisteredGenerated', 'GeneratedGeneric', 'Generic', 'Discovered', 'Assembly', 'Assemblies')
        if ($framework -eq 'net11.0') { $sites += @('Classifier', 'UnionSetup') }
        Assert-Diagnostics $output $mode $sites $locations
        if ($mode -eq 'plain') {
            $null = Invoke-DotNet @('run', '--project', $project, '-c', 'Release', '--no-build', '--no-restore', '-r', $RuntimeIdentifier) (Join-Path $directory 'run.log')
        }
        elseif ($Publish) {
            if ($mode -eq 'aot' -and $NativeCompileOnly) {
                # Run the actual NativeAOT compiler and its analysis, but do not invoke a linker.
                # This is useful on Windows without the C++ workload; CI uses the full publish.
                # The .NET 11 compiler consumes ResolvedFileToPublish; .NET 10 consumes copy-local assets.
                $targets = if ($framework -eq 'net11.0') { 'Build;ComputeResolvedFilesToPublishList;IlcCompile' } else { 'Build;_ComputeResolvedCopyLocalPublishAssets;_ComputeAssembliesToCompileToNative;IlcCompile' }
                $output = Invoke-DotNet @('msbuild', $project, "-t:$targets", '-p:Configuration=Release', '-p:SelfContained=true', "-p:RuntimeIdentifier=$RuntimeIdentifier", '-p:IlcUseEnvironmentalTools=true') (Join-Path $directory 'native-compile.log')
            }
            else {
                $output = Invoke-DotNet @('publish', $project, '-c', 'Release', '--no-restore', '-r', $RuntimeIdentifier) (Join-Path $directory 'publish.log')
            }
            Assert-Diagnostics $output $mode $sites $locations 'publish'
        }
        $stage = if ($mode -eq 'aot' -and $NativeCompileOnly) { 'native compilation (linking not verified)' } elseif ($Publish -and $mode -ne 'plain') { 'publish' } else { 'build/run' }
        Write-Host "PASS $framework $mode $stage"
        $evidence.Results += @{ Framework = $framework; Mode = $mode; Stage = $stage; Passed = $true }
        $evidence | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $run 'results.json')
    }
}
