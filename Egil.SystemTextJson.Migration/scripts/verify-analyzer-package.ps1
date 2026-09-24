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

try {
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
    $buildOutput = & dotnet build $projectPath -c Release -v:diag -warnaserror 2>&1 | Out-String

    if ($buildOutput -match 'CS803\d|AD0001') {
        throw "The package-only consumer reported an analyzer load failure.`n$buildOutput"
    }

    if ($LASTEXITCODE -ne 0) {
        throw "The package-only consumer build was not warning-free.`n$buildOutput"
    }

    if ($buildOutput -notmatch '(?s)ResolvedAnalyzers.*Egil\.SystemTextJson\.Migration\.Analyzers\.dll') {
        throw "The compiler trace did not resolve the packed analyzer.`n$buildOutput"
    }

    Write-Output "Verified '$analyzerAsset' and compiler resolution from a package-only consumer."
}
finally {
    if (Test-Path -LiteralPath $consumerDirectory) {
        $resolvedConsumerDirectory = [System.IO.Path]::GetFullPath($consumerDirectory).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)

        if ([System.IO.Path]::GetDirectoryName($resolvedConsumerDirectory) -ne $temporaryRoot -or
            [System.IO.Path]::GetFileName($resolvedConsumerDirectory) -ne $consumerLeaf) {
            throw "Refusing to remove unexpected consumer directory '$resolvedConsumerDirectory'."
        }

        Remove-Item -LiteralPath $resolvedConsumerDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
