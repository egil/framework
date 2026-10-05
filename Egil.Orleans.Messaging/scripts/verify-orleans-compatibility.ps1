[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackageDirectory,
    [string[]] $OrleansVersions = @('10.3.1', '10.4.0'),
    [string] $ArtifactsDirectory = (Join-Path $PSScriptRoot '../artifacts/compatibility'),
    [switch] $IncludeJournaling
)
$ErrorActionPreference = 'Stop'
$PackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$ArtifactsDirectory = [IO.Path]::GetFullPath($ArtifactsDirectory)
if ($OrleansVersions.Count -eq 0) { throw 'At least one Orleans host version is required' }
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packages = @{}
$stableIds = @('Egil.Orleans.Messaging', 'Egil.Orleans.Messaging.State.AzureStorage', 'Egil.Orleans.Messaging.Streams.EventHubs')

function Invoke-DotNet([string[]] $Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($file in Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.nupkg') {
    $archive = [IO.Compression.ZipFile]::OpenRead($file.FullName)
    try {
        $entry = @($archive.Entries | Where-Object FullName -Like '*.nuspec')
        if ($entry.Count -ne 1) { throw "Expected one nuspec in $file" }
        $reader = [IO.StreamReader]::new($entry[0].Open())
        try { [xml] $manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $manifest.package.metadata
        $id = [string] $metadata.id
        if ($id -notin ($stableIds + 'Egil.Orleans.Messaging.Journaling')) { continue }
        if ($packages.ContainsKey($id)) { throw "Multiple versions of $id in package directory" }
        $groups = @($metadata.dependencies.group)
        if ($groups.Count -ne 1 -or $groups[0].targetFramework -ne 'net10.0') { throw "$id must have one net10.0 dependency group" }
        $dependencies = @($groups[0].dependency)
        foreach ($dependency in $dependencies) {
            if ($id -in $stableIds -and $dependency.id -like 'Microsoft.Orleans.*' -and $dependency.version -ne '10.3.1') {
                throw "$id raises the supported Orleans floor: $($dependency.id) $($dependency.version)"
            }
        }
        $binary = $archive.GetEntry("lib/net10.0/$id.dll")
        if ($null -eq $binary) { throw "$id has no net10.0 binary" }
        $stream = $binary.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
        $packages[$id] = @{ Version = [string] $metadata.version; Dependencies = @($dependencies | ForEach-Object { @{ id = [string] $_.id; version = [string] $_.version } }); BinaryHash = $hash; PackageHash = (Get-FileHash -LiteralPath $file.FullName).Hash }
    } finally { $archive.Dispose() }
}
foreach ($id in $stableIds) {
    if (-not $packages.ContainsKey($id)) { throw "Missing stable package $id" }
}
$coreVersion = $packages['Egil.Orleans.Messaging'].Version
foreach ($id in $stableIds | Where-Object { $_ -ne 'Egil.Orleans.Messaging' }) {
    $coreDependency = @($packages[$id].Dependencies | Where-Object id -EQ 'Egil.Orleans.Messaging')
    if ($packages[$id].Version -ne $coreVersion -or $coreDependency.Count -ne 1 -or $coreDependency[0].version -ne $coreVersion) {
        throw "$id must match core package version and dependency $coreVersion"
    }
}
if ($IncludeJournaling) {
    $journal = $packages['Egil.Orleans.Messaging.Journaling']
    if ($null -eq $journal -or $journal.Version -ne "$($coreVersion.Split('-')[0])-preview") { throw 'Journaling must use the aligned core-preview version' }
    $journalDependency = @($journal.Dependencies | Where-Object id -EQ 'Microsoft.Orleans.Journaling')
    $coreDependency = @($journal.Dependencies | Where-Object id -EQ 'Egil.Orleans.Messaging')
    if ($journalDependency.Count -ne 1 -or $journalDependency[0].version -ne '10.4.0-alpha.1' -or $coreDependency.Count -ne 1 -or $coreDependency[0].version -ne $coreVersion) {
        throw 'Journaling must depend on Orleans 10.4.0-alpha.1 and the matching stable core'
    }
}
$testNames = @('Egil.Orleans.Messaging.Tests', 'Egil.Orleans.Messaging.State.Consumer.Tests', 'Egil.Orleans.Messaging.Streams.Consumer.Tests', 'Egil.Orleans.Messaging.State.AzureStorage.Tests', 'Egil.Orleans.Messaging.Streams.EventHubs.Tests')
New-Item -ItemType Directory -Path $ArtifactsDirectory -Force | Out-Null
foreach ($version in $OrleansVersions) {
    if ($version -notin @('10.3.1', '10.4.0')) { throw "Unsupported matrix version: $version" }
    # Each invocation gets a fresh cache: local packages can have the same version
    # after edits, and NuGet otherwise silently reuses an older binary.
    $runDirectory = Join-Path $ArtifactsDirectory "$version-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
    $configPath = Join-Path $runDirectory 'NuGet.Config'
    $escapedFeed = [Security.SecurityElement]::Escape($PackageDirectory)
    "<configuration><packageSources><clear/><add key=`"packed-om`" value=`"$escapedFeed`"/><add key=`"nuget.org`" value=`"https://api.nuget.org/v3/index.json`"/></packageSources></configuration>" | Set-Content -LiteralPath $configPath
    $hostTests = @($testNames)
    if ($IncludeJournaling -and $version -eq '10.4.0') { $hostTests += 'Egil.Orleans.Messaging.Journaling.Tests' }
    foreach ($name in $hostTests) {
        $project = Join-Path $projectRoot "test/$name/$name.csproj"
        $properties = @("-p:OrleansTestVersion=$version", "-p:MessagingPackageVersion=$coreVersion", "-p:MessagingJournalingPackageVersion=$($packages['Egil.Orleans.Messaging.Journaling'].Version)", "-p:RestorePackagesPath=$runDirectory/packages")
        Invoke-DotNet (@('restore', $project, '--artifacts-path', $runDirectory, '--configfile', $configPath) + $properties)
        $assetsPath = Join-Path $runDirectory "obj/$name/project.assets.json"
        $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
        if (@($assets.libraries.Values | Where-Object type -EQ 'project').Count -ne 0) { throw "$name still consumes a project reference" }
        foreach ($library in $assets.libraries.GetEnumerator()) {
            $expectedVersion = if ($library.Key -match '^Microsoft\.Orleans\.(Journaling|DurableJobs)/') { "$version-alpha.1" } else { $version }
            if ($library.Key -like 'Microsoft.Orleans.*/*' -and ($library.Key -split '/')[1] -ne $expectedVersion) {
                throw "$name resolved $($library.Key), expected Orleans $version"
            }
        }
        Invoke-DotNet (@('build', $project, '-c', 'Release', '--no-restore', '--artifacts-path', $runDirectory) + $properties)
        $output = Join-Path $runDirectory "bin/$name/release"
        foreach ($id in $packages.Keys) {
            if ($assets.libraries.ContainsKey("$id/$($packages[$id].Version)")) {
                $copy = Join-Path $output "$id.dll"
                if (-not (Test-Path -LiteralPath $copy)) { throw "$name did not copy the resolved $id binary" }
                if ((Get-FileHash -LiteralPath $copy).Hash -ne $packages[$id].BinaryHash) { throw "$name loaded a different $id binary" }
            }
        }
        Invoke-DotNet @('test', '--root-directory', $output, '--test-modules', "$name.dll", '--no-progress', '--minimum-expected-tests', '1', '--results-directory', (Join-Path $runDirectory "results/$name"))
    }
    @{ OrleansVersion = $version; Packages = $packages; Tests = $hostTests; Commit = (& git -C $projectRoot rev-parse HEAD); WorkingTree = @(& git -C $projectRoot status --porcelain); Infrastructure = 'In-process Orleans and provider adapter tests; no external broker or Azure storage restart proof' } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'evidence.json')
}
