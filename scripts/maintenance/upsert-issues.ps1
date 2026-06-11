[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PayloadDirectory,

    [Parameter(Mandatory = $true)]
    [string] $MetadataPath,

    [switch] $CloseResolved
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
    throw "GH_TOKEN must be set."
}

if ([string]::IsNullOrWhiteSpace($env:GITHUB_REPOSITORY)) {
    throw "GITHUB_REPOSITORY must be set."
}

$PayloadDirectory = (Resolve-Path $PayloadDirectory).Path
$MetadataPath = (Resolve-Path $MetadataPath).Path
$metadata = Get-Content -LiteralPath $MetadataPath -Raw | ConvertFrom-Json

$labelDefinitions = @(
    @{ name = "maintenance"; color = "5319e7"; description = "Automated maintenance inventory issue" },
    @{ name = "maintenance:dependencies"; color = "0366d6"; description = "Dependency update or dependency health work" },
    @{ name = "maintenance:sdk"; color = "1d76db"; description = "SDK or target framework maintenance" },
    @{ name = "maintenance:test-harness"; color = "0e8a16"; description = "Test runner, coverage, or test platform maintenance" },
    @{ name = "maintenance:ci"; color = "fbca04"; description = "GitHub Actions or CI validation maintenance" },
    @{ name = "maintenance:tools"; color = "c5def5"; description = "Dotnet local tool maintenance" },
    @{ name = "build"; color = "bfdadc"; description = "Repo build and tooling scope" },
    @{ name = "stjm"; color = "d4c5f9"; description = "Egil.SystemTextJson.Migration scope" },
    @{ name = "ot"; color = "d4c5f9"; description = "Egil.Orleans.Testing scope" },
    @{ name = "stp"; color = "d4c5f9"; description = "Egil.StronglyTypedPrimitives scope" }
)

foreach ($label in $labelDefinitions) {
    & gh label create $label.name --repo $env:GITHUB_REPOSITORY --color $label.color --description $label.description --force | Out-Null
}

function Get-IssueForKey {
    param([string] $Key)

    $query = "repo:$env:GITHUB_REPOSITORY in:body maintenance-inventory:key=$Key"
    $json = & gh search issues $query --state all --json number,state --limit 1
    if ([string]::IsNullOrWhiteSpace($json)) {
        return $null
    }

    $items = $json | ConvertFrom-Json
    if ($items.Count -eq 0) {
        return $null
    }

    return $items[0]
}

function Write-TempBody {
    param(
        [string] $Key,
        [string] $Body
    )

    $safeName = $Key -replace "[^A-Za-z0-9_.-]", "-"
    $path = Join-Path ([System.IO.Path]::GetTempPath()) "maintenance-$safeName.md"
    Set-Content -LiteralPath $path -Value $Body -Encoding utf8
    return $path
}

$activeKeys = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::OrdinalIgnoreCase)

Get-ChildItem -LiteralPath $PayloadDirectory -Filter "*.json" -File |
    Sort-Object Name |
    ForEach-Object {
        $payload = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
        [void]$activeKeys.Add([string]$payload.key)
        $bodyPath = Write-TempBody -Key $payload.key -Body $payload.body
        $issue = Get-IssueForKey -Key $payload.key

        if ($null -eq $issue) {
            $url = & gh issue create --repo $env:GITHUB_REPOSITORY --title $payload.title --body-file $bodyPath
            $number = ($url -split "/")[-1]
            Write-Host "Created issue #$number for $($payload.key)."
        }
        else {
            $number = $issue.number
            & gh issue edit $number --repo $env:GITHUB_REPOSITORY --title $payload.title --body-file $bodyPath | Out-Null
            if ($issue.state -eq "closed") {
                & gh issue reopen $number --repo $env:GITHUB_REPOSITORY | Out-Null
            }
            Write-Host "Updated issue #$number for $($payload.key)."
        }

        foreach ($label in @($payload.labels)) {
            if (-not [string]::IsNullOrWhiteSpace($label)) {
                & gh issue edit $number --repo $env:GITHUB_REPOSITORY --add-label $label | Out-Null
            }
        }
    }

if ($CloseResolved -and $metadata.canCloseResolved) {
    $query = "repo:$env:GITHUB_REPOSITORY is:issue state:open label:maintenance in:body maintenance-inventory:key="
    $openIssuesJson = & gh search issues $query --json number,body --limit 100
    $openIssues = $openIssuesJson | ConvertFrom-Json
    foreach ($issue in @($openIssues)) {
        $match = [regex]::Match([string]$issue.body, "maintenance-inventory:key=(?<key>[^ ]+)")
        if (-not $match.Success) {
            continue
        }

        $key = $match.Groups["key"].Value.Trim()
        if (-not $activeKeys.Contains($key)) {
            & gh issue close $issue.number --repo $env:GITHUB_REPOSITORY --comment "Maintenance inventory no longer reports this drift." | Out-Null
            Write-Host "Closed resolved maintenance issue #$($issue.number) for $key."
        }
    }
}
elseif ($CloseResolved) {
    Write-Host "Skipping resolved issue closure because the inventory was not complete."
}
