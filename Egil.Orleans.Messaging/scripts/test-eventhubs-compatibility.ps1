param([string] $OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot
if (!$OutputDirectory) { $OutputDirectory = [IO.Path]::GetTempPath() }
$OutputDirectory = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ('om-eventhubs-' + [guid]::NewGuid().ToString('N'))
$feed = Join-Path $OutputDirectory 'feed'
New-Item -ItemType Directory -Force $feed | Out-Null
function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
Invoke-Dotnet pack (Join-Path $projectRoot 'src/Egil.Orleans.Messaging/Egil.Orleans.Messaging.csproj') -c Release -o $feed
Invoke-Dotnet pack (Join-Path $projectRoot 'src/Egil.Orleans.Messaging.Streams.EventHubs/Egil.Orleans.Messaging.Streams.EventHubs.csproj') -c Release -o $feed
Add-Type -AssemblyName System.IO.Compression.FileSystem
$package = Get-ChildItem $feed -Filter 'Egil.Orleans.Messaging.Streams.EventHubs.*.nupkg' | Select-Object -First 1
$archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $entry = $archive.Entries | Where-Object FullName -Like '*.nuspec' | Select-Object -First 1
    $reader = [IO.StreamReader]::new($entry.Open())
    try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $dllEntry = $archive.GetEntry('lib/net10.0/Egil.Orleans.Messaging.Streams.EventHubs.dll')
    $dllStream = $dllEntry.Open()
    try { $expectedDllHash = (Get-FileHash -InputStream $dllStream -Algorithm SHA256).Hash } finally { $dllStream.Dispose() }
    $version = $nuspec.package.metadata.version
    $providerDependency = $nuspec.package.metadata.dependencies.group.dependency | Where-Object id -EQ 'Microsoft.Orleans.Streaming.EventHubs'
    if ($providerDependency.version -ne '10.3.1') { throw "Unexpected provider minimum: $($providerDependency.version)" }
} finally { $archive.Dispose() }
$hashBefore = (Get-FileHash $package.FullName -Algorithm SHA256).Hash
$sources = (Join-Path $projectRoot 'test/Egil.Orleans.Messaging.Streams.EventHubs.Tests').Replace('\', '/')
$recoverySources = (Join-Path $projectRoot 'test/EventHubs.Compatibility/*.cs').Replace('\', '/')
foreach ($orleansVersion in '10.3.1', '10.4.0') {
    $consumer = Join-Path $OutputDirectory $orleansVersion
    New-Item -ItemType Directory -Force $consumer | Out-Null
    $recovery = if ($orleansVersion -eq '10.4.0') { '<Compile Include="' + $recoverySources + '" />' } else { '' }
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems><IsTestProject>true</IsTestProject><OutputType>Exe</OutputType>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$sources/**/*.cs" Exclude="$sources/obj/**;$sources/bin/**" />$recovery
    <Using Include="Egil.Orleans.Messaging.Streams" /><Using Include="Egil.Orleans.Messaging.Streams.EventHubs" />
    <Using Include="Microsoft.Extensions.DependencyInjection" /><Using Include="Orleans" /><Using Include="Orleans.Hosting" /><Using Include="Xunit" />
    <PackageReference Include="Egil.Orleans.Messaging.Streams.EventHubs" Version="$version" />
    <PackageReference Include="Microsoft.Orleans.Server" Version="$orleansVersion" />
    <PackageReference Include="Microsoft.Orleans.Streaming.EventHubs" Version="$orleansVersion" />
    <PackageReference Include="Microsoft.Orleans.Sdk" Version="$orleansVersion" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.5.1" /><PackageReference Include="xunit.v3" Version="3.2.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>
</Project>
"@ | Set-Content (Join-Path $consumer 'Tests.csproj')
    @"
<configuration><packageSources><clear /><add key="artifact" value="$feed" /><add key="nuget" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>
"@ | Set-Content (Join-Path $consumer 'NuGet.Config')
    Invoke-Dotnet restore (Join-Path $consumer 'Tests.csproj') --configfile (Join-Path $consumer 'NuGet.Config') --packages (Join-Path $OutputDirectory 'packages')
    Invoke-Dotnet test (Join-Path $consumer 'Tests.csproj') -c Release --no-restore
    $loadedDll = Join-Path $consumer 'bin/Release/net10.0/Egil.Orleans.Messaging.Streams.EventHubs.dll'
    if ((Get-FileHash $loadedDll -Algorithm SHA256).Hash -ne $expectedDllHash) { throw 'Consumer did not load the packed assembly.' }
}
if ((Get-FileHash $package.FullName -Algorithm SHA256).Hash -ne $hashBefore) { throw 'Package changed between consumer runs.' }
Write-Host "Same artifact verified on both Orleans versions: $hashBefore"
Write-Host "Evidence directory: $OutputDirectory"
