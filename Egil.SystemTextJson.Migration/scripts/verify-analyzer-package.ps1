[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$package = Get-Item -LiteralPath $PackagePath
$analyzerAsset = 'analyzers/dotnet/cs/Egil.SystemTextJson.Migration.Analyzers.dll'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)

try {
    if (-not ($archive.Entries.FullName -contains $analyzerAsset)) {
        throw "Package '$($package.Name)' does not contain '$analyzerAsset'."
    }
}
finally {
    $archive.Dispose()
}

$packagePrefix = 'Egil.SystemTextJson.Migration.'
$packageVersion = $package.BaseName.Substring($packagePrefix.Length)
$temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$consumerLeaf = "stjm-analyzer-consumer-$([guid]::NewGuid())"
$consumerDirectory = [System.IO.Path]::GetFullPath((Join-Path $temporaryRoot $consumerLeaf))
$invalidConsumerLeaf = "stjm-analyzer-invalid-consumer-$([guid]::NewGuid())"
$invalidConsumerDirectory = [System.IO.Path]::GetFullPath((Join-Path $temporaryRoot $invalidConsumerLeaf))
$severityConsumerLeaf = "stjm-analyzer-severity-consumer-$([guid]::NewGuid())"
$severityConsumerDirectory = [System.IO.Path]::GetFullPath((Join-Path $temporaryRoot $severityConsumerLeaf))
$packagesLeaf = "stjm-analyzer-packages-$([guid]::NewGuid())"
$packagesDirectory = [System.IO.Path]::GetFullPath((Join-Path $temporaryRoot $packagesLeaf))
$hadNugetPackages = Test-Path Env:NUGET_PACKAGES
$previousNugetPackages = if ($hadNugetPackages) { $env:NUGET_PACKAGES } else { $null }

function Remove-VerifiedTemporaryDirectory([string]$directory, [string]$leaf) {
    if (-not (Test-Path -LiteralPath $directory)) {
        return
    }

    $resolvedDirectory = [System.IO.Path]::GetFullPath($directory).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)

    if ([System.IO.Path]::GetDirectoryName($resolvedDirectory) -ne $temporaryRoot -or
        [System.IO.Path]::GetFileName($resolvedDirectory) -ne $leaf) {
        throw "Refusing to remove unexpected temporary directory '$resolvedDirectory'."
    }

    Remove-Item -LiteralPath $resolvedDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

try {
    New-Item -ItemType Directory -Path $packagesDirectory | Out-Null
    $env:NUGET_PACKAGES = $packagesDirectory
    New-Item -ItemType Directory -Path $consumerDirectory | Out-Null
    $projectPath = Join-Path $consumerDirectory 'Consumer.csproj'
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <RestoreSources>$($package.Directory.FullName)</RestoreSources>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Egil.SystemTextJson.Migration" Version="$packageVersion" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $projectPath

    'Console.WriteLine("consumer");' | Set-Content -LiteralPath (Join-Path $consumerDirectory 'Program.cs')
    & dotnet restore $projectPath --no-http-cache | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "The package-only consumer restore failed." }
    $buildOutput = & dotnet build $projectPath -c Release -v:diag -warnaserror --no-restore 2>&1 | Out-String

    if ($buildOutput -match 'CS803\d|AD0001') {
        throw "The package-only consumer reported an analyzer load failure.`n$buildOutput"
    }

    if ($LASTEXITCODE -ne 0) {
        throw "The package-only consumer build was not warning-free.`n$buildOutput"
    }

    if ($buildOutput -notmatch '(?s)ResolvedAnalyzers.*Egil\.SystemTextJson\.Migration\.Analyzers\.dll') {
        throw "The compiler trace did not resolve the packed analyzer.`n$buildOutput"
    }

    New-Item -ItemType Directory -Path $invalidConsumerDirectory | Out-Null
    $invalidProjectPath = Join-Path $invalidConsumerDirectory 'InvalidConsumer.csproj'
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <RestoreSources>$($package.Directory.FullName)</RestoreSources>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Egil.SystemTextJson.Migration" Version="$packageVersion" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $invalidProjectPath

    @"
using Egil.SystemTextJson.Migration;

[JsonMigratable]
public sealed class Target : IMigrate<Source, Target>
{
    public bool TryMigrateFrom(Source source, out Target result)
    {
        result = this;
        return true;
    }
}

public sealed class Source
{
}
"@ | Set-Content -LiteralPath (Join-Path $invalidConsumerDirectory 'Program.cs')
    & dotnet restore $invalidProjectPath --no-http-cache | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "The invalid package-only consumer restore failed." }
    $invalidBuildOutput = & dotnet build $invalidProjectPath -c Release -v:diag --no-restore 2>&1 | Out-String

    if ($LASTEXITCODE -ne 0) {
        throw "The invalid package-only consumer did not build successfully.`n$invalidBuildOutput"
    }

    if ($invalidBuildOutput -notmatch 'STJM0002') {
        throw "The invalid package-only consumer did not report STJM0002.`n$invalidBuildOutput"
    }

    New-Item -ItemType Directory -Path $severityConsumerDirectory | Out-Null
    $severityProjectPath = Join-Path $severityConsumerDirectory 'SeverityConsumer.csproj'
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <RestoreSources>$($package.Directory.FullName)</RestoreSources>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Egil.SystemTextJson.Migration" Version="$packageVersion" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $severityProjectPath

    @"
using Egil.SystemTextJson.Migration;

[JsonMigrationLegacyType]
public enum OrphanedLegacyPayload
{
    Value,
}

public sealed class OrdinaryUse
{
    public OrphanedLegacyPayload Value => OrphanedLegacyPayload.Value;
}
"@ | Set-Content -LiteralPath (Join-Path $severityConsumerDirectory 'Program.cs')

    @"
root = true

[*.cs]
dotnet_diagnostic.STJM0011.severity = error
dotnet_diagnostic.STJM0010.severity = none
"@ | Set-Content -LiteralPath (Join-Path $severityConsumerDirectory '.editorconfig')
    & dotnet restore $severityProjectPath --no-http-cache | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "The severity consumer restore failed." }
    $stjm0011BuildOutput = & dotnet build $severityProjectPath -c Release --no-restore 2>&1 | Out-String

    if ($LASTEXITCODE -eq 0 -or $stjm0011BuildOutput -notmatch 'error STJM0011' -or $stjm0011BuildOutput -match 'STJM0010') {
        throw "The package-only consumer did not honor STJM0011=error and STJM0010=none from .editorconfig.`n$stjm0011BuildOutput"
    }

    @"
root = true

[*.cs]
dotnet_diagnostic.STJM0011.severity = none
dotnet_diagnostic.STJM0010.severity = error
"@ | Set-Content -LiteralPath (Join-Path $severityConsumerDirectory '.editorconfig')
    $stjm0010BuildOutput = & dotnet build $severityProjectPath -c Release --no-restore 2>&1 | Out-String

    if ($LASTEXITCODE -eq 0 -or $stjm0010BuildOutput -notmatch 'error STJM0010' -or $stjm0010BuildOutput -match 'STJM0011') {
        throw "The package-only consumer did not honor STJM0011=none and STJM0010=error from .editorconfig.`n$stjm0010BuildOutput"
    }

    Write-Output "Verified '$analyzerAsset' and compiler resolution from a package-only consumer."
    Write-Output "Verified STJM0002 from a deliberately invalid package-only consumer."
    Write-Output "Verified independent STJM0010 and STJM0011 .editorconfig severities from a package-only consumer."
}
finally {
    if ($hadNugetPackages) {
        $env:NUGET_PACKAGES = $previousNugetPackages
    }
    else {
        Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue
    }

    Remove-VerifiedTemporaryDirectory $consumerDirectory $consumerLeaf
    Remove-VerifiedTemporaryDirectory $invalidConsumerDirectory $invalidConsumerLeaf
    Remove-VerifiedTemporaryDirectory $severityConsumerDirectory $severityConsumerLeaf
    Remove-VerifiedTemporaryDirectory $packagesDirectory $packagesLeaf
}
