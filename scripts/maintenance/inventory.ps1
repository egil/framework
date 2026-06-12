[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path,
    [string] $OutputDirectory = (Join-Path $RepositoryRoot "artifacts/maintenance"),
    [switch] $SkipPackageQueries,
    [switch] $IncludePrerelease
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepositoryRoot = (Resolve-Path $RepositoryRoot).Path
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$IssueDirectory = Join-Path $OutputDirectory "issues"
$PackageListDirectory = Join-Path $OutputDirectory "package-list"

New-Item -ItemType Directory -Force -Path $OutputDirectory, $IssueDirectory, $PackageListDirectory | Out-Null
Get-ChildItem -LiteralPath $IssueDirectory -File -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem -LiteralPath $PackageListDirectory -File -ErrorAction SilentlyContinue | Remove-Item -Force

$baselinePath = Join-Path $RepositoryRoot "docs/maintenance/baseline.json"
if (-not (Test-Path -LiteralPath $baselinePath)) {
    throw "Missing maintenance baseline config: $baselinePath"
}

$baseline = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
$scopeMap = @{}
$baseline.scopeMap.PSObject.Properties | ForEach-Object { $scopeMap[$_.Name] = [string]$_.Value }
$sharedPackages = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$baseline.sharedBaselinePackages | ForEach-Object { [void]$sharedPackages.Add([string]$_) }

$findings = [ordered]@{}
$packageQueryErrors = New-Object System.Collections.Generic.List[object]

function ConvertTo-RepoPath {
    param([string] $Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    return [System.IO.Path]::GetRelativePath($RepositoryRoot, $fullPath).Replace("\", "/")
}

function Get-ScopeFromPath {
    param([string] $Path)

    $relativePath = ConvertTo-RepoPath $Path
    $firstSegment = ($relativePath -split "/")[0]
    if ($scopeMap.ContainsKey($firstSegment)) {
        return $scopeMap[$firstSegment]
    }

    if ($relativePath.StartsWith(".github/", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "ci"
    }

    return "build"
}

function New-Finding {
    param(
        [string] $Key,
        [string] $Title,
        [string] $Scope,
        [string] $Category,
        [string[]] $Labels,
        [string] $Summary
    )

    if (-not $findings.Contains($Key)) {
        $allLabels = New-Object System.Collections.Generic.List[string]
        @("maintenance", "maintenance:$Category") + $Labels | ForEach-Object {
            if (-not [string]::IsNullOrWhiteSpace($_) -and -not $allLabels.Contains($_)) {
                $allLabels.Add($_)
            }
        }

        $findings[$Key] = [ordered]@{
            key = $Key
            title = $Title
            scope = $Scope
            category = $Category
            labels = @($allLabels)
            summary = $Summary
            details = New-Object System.Collections.Generic.List[string]
            acceptance = New-Object System.Collections.Generic.List[string]
        }
    }

    return $findings[$Key]
}

function Add-FindingDetail {
    param(
        [string] $Key,
        [string] $Title,
        [string] $Scope,
        [string] $Category,
        [string[]] $Labels,
        [string] $Summary,
        [string] $Detail,
        [string[]] $Acceptance = @()
    )

    $finding = New-Finding -Key $Key -Title $Title -Scope $Scope -Category $Category -Labels $Labels -Summary $Summary
    if (-not [string]::IsNullOrWhiteSpace($Detail)) {
        $finding.details.Add($Detail)
    }
    foreach ($item in $Acceptance) {
        if (-not $finding.acceptance.Contains($item)) {
            $finding.acceptance.Add($item)
        }
    }
}

function Get-ProjectDisplayName {
    param([string] $Scope)

    switch ($Scope) {
        "stjm" { return "Egil.SystemTextJson.Migration" }
        "ot" { return "Egil.Orleans.Testing" }
        "stp" { return "Egil.StronglyTypedPrimitives" }
        "ci" { return "CI" }
        default { return "repo" }
    }
}

function Get-SafeFileName {
    param([string] $Value)

    return ($Value -replace "[^A-Za-z0-9_.-]", "-")
}

function Format-MarkdownCode {
    param([object] $Value)

    return "``$Value``"
}

function Get-XmlAttributeValue {
    param(
        [System.Xml.XmlNode] $Node,
        [string] $Name
    )

    if ($null -eq $Node -or $null -eq $Node.Attributes -or $null -eq $Node.Attributes[$Name]) {
        return ""
    }

    return [string]$Node.Attributes[$Name].Value
}

function Get-ObjectPropertyValue {
    param(
        [object] $Object,
        [string] $Name,
        [string] $Default = ""
    )

    if ($null -eq $Object) {
        return $Default
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $Default
    }

    $value = $property.Value
    if ($null -eq $value) {
        return $Default
    }

    if ($value -is [string] -and [string]::IsNullOrWhiteSpace($value)) {
        return $Default
    }

    return [string]$value
}

function Get-JsonFromCommandOutput {
    param([string] $Text)

    $start = $Text.IndexOf("{", [System.StringComparison]::Ordinal)
    if ($start -lt 0) {
        return $null
    }

    return $Text.Substring($start) | ConvertFrom-Json
}

function Get-PackageRows {
    param(
        [object] $Json,
        [string] $Kind,
        [string] $Scope
    )

    $rows = New-Object System.Collections.Generic.List[object]
    if ($null -eq $Json.projects) {
        return $rows
    }

    foreach ($project in @($Json.projects)) {
        if (-not ($project.PSObject.Properties.Name -contains "frameworks")) {
            continue
        }

        foreach ($framework in @($project.frameworks)) {
            foreach ($collectionName in @("topLevelPackages", "transitivePackages")) {
                if (-not ($framework.PSObject.Properties.Name -contains $collectionName)) {
                    continue
                }

                foreach ($package in @($framework.$collectionName)) {
                    $id = [string]$package.id
                    if ([string]::IsNullOrWhiteSpace($id)) {
                        continue
                    }

                    $row = [ordered]@{
                        scope = $Scope
                        project = Get-ObjectPropertyValue -Object $project -Name "path"
                        framework = Get-ObjectPropertyValue -Object $framework -Name "framework" -Default "(unknown framework)"
                        package = $id
                        kind = $Kind
                        requested = Get-ObjectPropertyValue -Object $package -Name "requestedVersion"
                        resolved = Get-ObjectPropertyValue -Object $package -Name "resolvedVersion"
                        latest = Get-ObjectPropertyValue -Object $package -Name "latestVersion"
                        collection = $collectionName
                    }

                    if ($Kind -eq "outdated" -and -not [string]::IsNullOrWhiteSpace($row.latest)) {
                        $rows.Add([pscustomobject]$row)
                    }
                    elseif ($Kind -eq "deprecated" -and ($package.PSObject.Properties.Name -contains "deprecationReasons")) {
                        $row.reason = (@($package.deprecationReasons) -join ", ")
                        $rows.Add([pscustomobject]$row)
                    }
                    elseif ($Kind -eq "vulnerable" -and ($package.PSObject.Properties.Name -contains "vulnerabilities")) {
                        $row.reason = (@($package.vulnerabilities | ForEach-Object { "$($_.severity): $($_.advisoryUrl)" }) -join ", ")
                        $rows.Add([pscustomobject]$row)
                    }
                }
            }
        }
    }

    return $rows
}

$packageVersionRecords = New-Object System.Collections.Generic.List[object]
$targetFrameworkRecords = New-Object System.Collections.Generic.List[object]
$toolRecords = New-Object System.Collections.Generic.List[object]
$workflowRecords = New-Object System.Collections.Generic.List[object]
$packageQueryRows = New-Object System.Collections.Generic.List[object]

Get-ChildItem -LiteralPath $RepositoryRoot -Recurse -File -Filter "Directory.Packages.props" |
    Where-Object { $_.FullName -notmatch "[\\/](bin|obj)[\\/]" } |
    ForEach-Object {
        [xml]$xml = Get-Content -LiteralPath $_.FullName -Raw
        $scope = Get-ScopeFromPath $_.FullName
        foreach ($node in $xml.SelectNodes("//PackageVersion|//GlobalPackageVersion")) {
            $include = Get-XmlAttributeValue -Node $node -Name "Include"
            if ([string]::IsNullOrWhiteSpace($include)) {
                continue
            }

            $packageVersionRecords.Add([pscustomobject]@{
                scope = $scope
                file = ConvertTo-RepoPath $_.FullName
                kind = $node.Name
                package = $include
                version = Get-XmlAttributeValue -Node $node -Name "Version"
                condition = Get-XmlAttributeValue -Node $node.ParentNode -Name "Condition"
            })
        }
    }

$packageVersionRecords |
    Where-Object { $sharedPackages.Contains([string]$_.package) } |
    Group-Object package |
    Where-Object { @($_.Group.version | Sort-Object -Unique).Count -gt 1 } |
    Sort-Object Name |
    ForEach-Object {
        $detail = ($_.Group | Sort-Object scope, file, condition | ForEach-Object {
            $condition = if ([string]::IsNullOrWhiteSpace($_.condition)) { "" } else { " ($($_.condition))" }
            "- $($_.package): $(Format-MarkdownCode $_.version) in $(Format-MarkdownCode $_.file)$condition"
        }) -join "`n"

        Add-FindingDetail `
            -Key "dependencies-build-shared-baseline" `
            -Title "maintenance(build): align shared dependency baselines" `
            -Scope "build" `
            -Category "dependencies" `
            -Labels @("build") `
            -Summary "Shared build, analyzer, and test package versions differ across package baselines." `
            -Detail "### $($_.Name)`n$detail" `
            -Acceptance @(
                "Shared baseline packages use the same version unless a package-specific exception is documented.",
                "Any intentional exception is recorded in docs/maintenance/baseline.json or the issue body."
            )
    }

Get-ChildItem -LiteralPath $RepositoryRoot -Recurse -File -Filter "*.csproj" |
    Where-Object { $_.FullName -notmatch "[\\/](bin|obj)[\\/]" } |
    ForEach-Object {
        [xml]$xml = Get-Content -LiteralPath $_.FullName -Raw
        $targets = New-Object System.Collections.Generic.List[string]
        foreach ($propertyGroup in @($xml.Project.PropertyGroup)) {
            $targetFramework = $propertyGroup.SelectSingleNode("TargetFramework")
            $targetFrameworks = $propertyGroup.SelectSingleNode("TargetFrameworks")
            if ($null -ne $targetFramework) {
                $targets.Add([string]$targetFramework.InnerText)
            }
            if ($null -ne $targetFrameworks) {
                ([string]$targetFrameworks.InnerText -split ";") | ForEach-Object {
                    if (-not [string]::IsNullOrWhiteSpace($_)) {
                        $targets.Add($_)
                    }
                }
            }
        }

        if ($targets.Count -eq 0) {
            return
        }

        $scope = Get-ScopeFromPath $_.FullName
        $record = [pscustomobject]@{
            scope = $scope
            file = ConvertTo-RepoPath $_.FullName
            targetFrameworks = @($targets | Sort-Object -Unique)
        }
        $targetFrameworkRecords.Add($record)

        $watchTargets = @($record.targetFrameworks | Where-Object { $_ -in @($baseline.targetFrameworkPolicy.watchTargets) })
        $compatTargets = @($record.targetFrameworks | Where-Object { $_ -in @($baseline.targetFrameworkPolicy.compatibilityTargets) })
        if ($watchTargets.Count -gt 0 -or $compatTargets.Count -gt 0) {
            $scopeName = Get-ProjectDisplayName $scope
            Add-FindingDetail `
                -Key "sdk-$scope" `
                -Title "maintenance($scope): review target framework support" `
                -Scope $scope `
                -Category "sdk" `
                -Labels @($scope) `
                -Summary "This package targets frameworks that should stay intentional as the repo baseline moves toward net10.0." `
                -Detail "- $(Format-MarkdownCode $record.file) targets $(Format-MarkdownCode (@($record.targetFrameworks) -join ';')) ." `
                -Acceptance @(
                    "$scopeName has an explicit decision for each net9.0, net8.0, or netstandard2.0 target.",
                    "CI installs only SDKs required by the retained target frameworks."
                )
        }
    }

Get-ChildItem -LiteralPath $RepositoryRoot -Recurse -File |
    Where-Object { $_.Name -ieq "global.json" } |
    Where-Object { $_.FullName -notmatch "[\\/](bin|obj)[\\/]" } |
    ForEach-Object {
        $scope = Get-ScopeFromPath $_.FullName
        $json = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
        $sdkVersion = if ($json.sdk.PSObject.Properties.Name -contains "version") { [string]$json.sdk.version } else { "(roll-forward only)" }
        Add-FindingDetail `
            -Key "sdk-$scope" `
            -Title "maintenance($scope): review target framework support" `
            -Scope $scope `
            -Category "sdk" `
            -Labels @($scope) `
            -Summary "This package has SDK selection policy outside the repo root." `
            -Detail "- $(Format-MarkdownCode (ConvertTo-RepoPath $_.FullName)) declares SDK $(Format-MarkdownCode $sdkVersion) with rollForward $(Format-MarkdownCode $json.sdk.rollForward)." `
            -Acceptance @("SDK selection is either moved to a repo-wide policy or documented as package-specific.")
    }

Get-ChildItem -LiteralPath $RepositoryRoot -Recurse -File -Filter "dotnet-tools.json" |
    Where-Object { $_.FullName -notmatch "[\\/](bin|obj)[\\/]" } |
    ForEach-Object {
        $scope = Get-ScopeFromPath $_.FullName
        $json = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
        foreach ($tool in $json.tools.PSObject.Properties) {
            $toolRecords.Add([pscustomobject]@{
                scope = $scope
                file = ConvertTo-RepoPath $_.FullName
                tool = [string]$tool.Name
                version = [string]$tool.Value.version
                rollForward = [string]$tool.Value.rollForward
            })
        }
    }

$toolRecords |
    Group-Object tool |
    Where-Object { @($_.Group.version | Sort-Object -Unique).Count -gt 1 } |
    Sort-Object Name |
    ForEach-Object {
        $detail = ($_.Group | Sort-Object file | ForEach-Object {
            "- $($_.tool): $(Format-MarkdownCode $_.version) in $(Format-MarkdownCode $_.file)"
        }) -join "`n"

        Add-FindingDetail `
            -Key "tools-build-dotnet-local-tools" `
            -Title "maintenance(build): align dotnet local tools" `
            -Scope "build" `
            -Category "tools" `
            -Labels @("build") `
            -Summary "Dotnet local tool manifests pin different versions for shared repo tooling." `
            -Detail "### $($_.Name)`n$detail" `
            -Acceptance @(
                "Shared local tools use the same version unless a package-specific exception is documented.",
                "Renovate can update dotnet-tools.json manifests without manual version discovery."
            )
    }

$workflowScopePatterns = @{
    "egil-systemtextjson-migration" = "stjm"
    "egil-orleans-testing" = "ot"
    "egil-strongly-typed-primitives" = "stp"
}

Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot ".github/workflows") -File |
    Where-Object { $_.Extension -in @(".yml", ".yaml") } |
    ForEach-Object {
        $text = Get-Content -LiteralPath $_.FullName -Raw
        $workflowName = $_.BaseName
        $scope = "ci"
        foreach ($entry in $workflowScopePatterns.GetEnumerator()) {
            if ($workflowName.Contains($entry.Key, [System.StringComparison]::OrdinalIgnoreCase)) {
                $scope = $entry.Value
                break
            }
        }

        $usesDotnet = $text.Contains("actions/setup-dotnet", [System.StringComparison]::OrdinalIgnoreCase)
        $runsTests = $text.Contains("dotnet test", [System.StringComparison]::OrdinalIgnoreCase)
        $hasRunTestJob = $text -match "(?m)^\s*run-test\s*:"
        $dotnetVersions = @([regex]::Matches($text, "dotnet-version:\s*(?<version>[^\r\n]+)") | ForEach-Object { $_.Groups["version"].Value.Trim() })
        $hasMultiLineDotnetVersion = $text -match "dotnet-version:\s*\|"

        $workflowRecords.Add([pscustomobject]@{
            scope = $scope
            file = ConvertTo-RepoPath $_.FullName
            usesDotnet = $usesDotnet
            runsTests = $runsTests
            hasRunTestJob = $hasRunTestJob
            dotnetVersions = $dotnetVersions
            hasMultiLineDotnetVersion = $hasMultiLineDotnetVersion
        })

        if ($scope -ne "ci" -and (-not $runsTests -or -not $hasRunTestJob)) {
            Add-FindingDetail `
                -Key "ci-$scope" `
                -Title "maintenance($scope): align package CI validation" `
                -Scope $scope `
                -Category "ci" `
                -Labels @($scope) `
                -Summary "Package CI should restore, build, test, pack, and validate unless an exception is documented." `
                -Detail "- $(Format-MarkdownCode (ConvertTo-RepoPath $_.FullName)) does not contain a dedicated $(Format-MarkdownCode 'run-test') job with $(Format-MarkdownCode 'dotnet test')." `
                -Acceptance @("The package workflow runs tests or documents why tests are not applicable.")
        }

        if ($scope -ne "ci" -and $usesDotnet -and ($text -match "dotnet-version:\s*\|" -or $dotnetVersions.Count -gt 0)) {
            Add-FindingDetail `
                -Key "ci-$scope" `
                -Title "maintenance($scope): align package CI validation" `
                -Scope $scope `
                -Category "ci" `
                -Labels @($scope) `
                -Summary "CI SDK bands should match the package target framework policy." `
                -Detail "- $(Format-MarkdownCode (ConvertTo-RepoPath $_.FullName)) hard-codes setup-dotnet SDK versions; verify they still match the retained TFMs." `
                -Acceptance @("CI SDK versions match the target frameworks retained by the package.")
        }
    }

$packageVersionRecords |
    Where-Object { $_.package -in @("xunit.v3", "xunit.runner.visualstudio", "xunit.v3.mtp-v2", "Microsoft.NET.Test.Sdk", "Microsoft.Testing.Extensions.CodeCoverage", "coverlet.msbuild") } |
    Group-Object scope |
    ForEach-Object {
        $scope = $_.Name
        $records = $_.Group
        $hasClassicRunner = @($records | Where-Object { $_.package -in @("Microsoft.NET.Test.Sdk", "xunit.runner.visualstudio") }).Count -gt 0
        $hasMtpRunner = @($records | Where-Object { $_.package -eq "xunit.v3.mtp-v2" }).Count -gt 0
        $hasOldXunit = @($records | Where-Object { $_.package -eq "xunit.v3" -and $_.version -match "^2\." }).Count -gt 0

        if ($hasClassicRunner -or -not $hasMtpRunner -or $hasOldXunit) {
            $detail = ($records | Sort-Object package | ForEach-Object {
                "- $($_.package) $(Format-MarkdownCode $_.version) in $(Format-MarkdownCode $_.file)"
            }) -join "`n"

            Add-FindingDetail `
                -Key "test-harness-$scope" `
                -Title "maintenance($scope): review test harness baseline" `
                -Scope $scope `
                -Category "test-harness" `
                -Labels @($scope) `
                -Summary "The package test harness differs from the preferred xUnit v3 plus Microsoft Testing Platform baseline." `
                -Detail $detail `
                -Acceptance @(
                    "The package either uses xUnit v3 plus Microsoft Testing Platform or documents why the classic runner remains.",
                    "Coverage tooling remains compatible with the chosen runner."
                )
        }
    }

if (-not $SkipPackageQueries) {
    $solutions = Get-ChildItem -LiteralPath $RepositoryRoot -Recurse -File -Include "*.sln", "*.slnx" |
        Where-Object { $_.FullName -notmatch "[\\/](bin|obj)[\\/]" } |
        Sort-Object FullName

    foreach ($solution in $solutions) {
        $scope = Get-ScopeFromPath $solution.FullName
        foreach ($kind in @("outdated", "deprecated", "vulnerable")) {
            $arguments = @("package", "list", "--project", $solution.FullName, "--$kind", "--format", "json", "--output-version", "1")
            if ($kind -eq "outdated" -and $IncludePrerelease) {
                $arguments += "--include-prerelease"
            }

            $safeName = Get-SafeFileName "$scope-$kind-$($solution.BaseName)"
            $rawPath = Join-Path $PackageListDirectory "$safeName.txt"
            $jsonPath = Join-Path $PackageListDirectory "$safeName.json"

            try {
                $output = & dotnet @arguments 2>&1
                $exitCode = $LASTEXITCODE
                $text = ($output | ForEach-Object { [string]$_ }) -join "`n"
                Set-Content -LiteralPath $rawPath -Value $text -Encoding utf8

                if ($exitCode -ne 0) {
                    throw "dotnet $($arguments -join ' ') exited with code $exitCode."
                }

                $json = Get-JsonFromCommandOutput $text
                if ($null -eq $json) {
                    throw "dotnet package list did not return JSON."
                }

                $json | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $jsonPath -Encoding utf8
                $rows = Get-PackageRows -Json $json -Kind $kind -Scope $scope
                foreach ($row in $rows) {
                    $packageQueryRows.Add($row)
                }
            }
            catch {
                $packageQueryErrors.Add([pscustomobject]@{
                    scope = $scope
                    solution = ConvertTo-RepoPath $solution.FullName
                    kind = $kind
                    message = $_.Exception.Message
                    rawOutput = ConvertTo-RepoPath $rawPath
                })

                Add-FindingDetail `
                    -Key "dependencies-$scope" `
                    -Title "maintenance($scope): review dependency updates" `
                    -Scope $scope `
                    -Category "dependencies" `
                    -Labels @($scope) `
                    -Summary "Dependency inventory could not fully evaluate this package." `
                    -Detail "- $(Format-MarkdownCode $kind) query failed for $(Format-MarkdownCode (ConvertTo-RepoPath $solution.FullName)): $($_.Exception.Message)" `
                    -Acceptance @("Dependency inventory commands run successfully for the package solution.")
            }
        }
    }

    $packageQueryRows |
        Group-Object scope, kind |
        ForEach-Object {
            $scope = ($_.Name -split ", ")[0]
            $kind = ($_.Name -split ", ")[1]
            $details = ($_.Group | Sort-Object package, framework | Select-Object -First 30 | ForEach-Object {
                $packageName = Get-ObjectPropertyValue -Object $_ -Name "package" -Default "(unknown package)"
                $framework = Get-ObjectPropertyValue -Object $_ -Name "framework" -Default "(unknown framework)"
                $resolved = Get-ObjectPropertyValue -Object $_ -Name "resolved" -Default "(unknown)"
                $latest = Get-ObjectPropertyValue -Object $_ -Name "latest" -Default "(unknown)"
                $reason = Get-ObjectPropertyValue -Object $_ -Name "reason"
                $versionText = switch ($kind) {
                    "outdated" { "$(Format-MarkdownCode $resolved) -> $(Format-MarkdownCode $latest)" }
                    default { "$(Format-MarkdownCode $resolved) $reason" }
                }
                "- $packageName $versionText in $(Format-MarkdownCode $framework)"
            }) -join "`n"

            Add-FindingDetail `
                -Key "dependencies-$scope" `
                -Title "maintenance($scope): review dependency updates" `
                -Scope $scope `
                -Category "dependencies" `
                -Labels @($scope) `
                -Summary "Dependency inventory found package updates or package health findings." `
                -Detail "### $kind`n$details" `
                -Acceptance @("Dependency updates are handled by Renovate PRs or a focused manual PR.")
        }
}

$generatedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$issuePayloads = New-Object System.Collections.Generic.List[object]

foreach ($finding in $findings.Values) {
    $detailsText = if ($finding.details.Count -gt 0) {
        ($finding.details | ForEach-Object { [string]$_ }) -join "`n`n"
    }
    else {
        "_No details captured._"
    }

    $acceptanceText = if ($finding.acceptance.Count -gt 0) {
        ($finding.acceptance | ForEach-Object { "- [ ] $_" }) -join "`n"
    }
    else {
        "- [ ] Drift is resolved or documented."
    }

    $body = @"
<!-- maintenance-inventory:key=$($finding.key) -->

Generated by the maintenance inventory on $generatedAt.

## Summary

$($finding.summary)

## Findings

$detailsText

## Suggested Agent Task

- Inspect the current package, SDK, workflow, or test-harness docs before changing code.
- Make one focused PR for this issue.
- Keep package release-note conventions intact; use `[skip notes]` for internal maintenance if needed.

## Acceptance Criteria

$acceptanceText
"@

    $payload = [ordered]@{
        key = $finding.key
        title = $finding.title
        scope = $finding.scope
        category = $finding.category
        labels = @($finding.labels)
        body = $body
    }

    $issuePayloads.Add([pscustomobject]$payload)
    $payloadPath = Join-Path $IssueDirectory "$((Get-SafeFileName $finding.key)).json"
    $payload | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $payloadPath -Encoding utf8
}

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# Maintenance Inventory")
$reportLines.Add("")
$reportLines.Add("Generated: $generatedAt")
$reportLines.Add("")
$reportLines.Add("## Findings")
$reportLines.Add("")
if ($issuePayloads.Count -eq 0) {
    $reportLines.Add("No actionable maintenance drift found.")
}
else {
    $reportLines.Add("| Scope | Category | Issue |")
    $reportLines.Add("|---|---|---|")
    foreach ($payload in $issuePayloads | Sort-Object scope, category, title) {
        $reportLines.Add("| $(Format-MarkdownCode $payload.scope) | $(Format-MarkdownCode $payload.category) | $($payload.title) |")
    }
}

$reportLines.Add("")
$reportLines.Add("## Shared Package Versions")
$reportLines.Add("")
$reportLines.Add("| Scope | Package | Version | File |")
$reportLines.Add("|---|---|---|---|")
foreach ($record in $packageVersionRecords | Sort-Object package, scope, file) {
    $reportLines.Add("| $(Format-MarkdownCode $record.scope) | $(Format-MarkdownCode $record.package) | $(Format-MarkdownCode $record.version) | $(Format-MarkdownCode $record.file) |")
}

$reportLines.Add("")
$reportLines.Add("## Target Frameworks")
$reportLines.Add("")
$reportLines.Add("| Scope | Project | Target Frameworks |")
$reportLines.Add("|---|---|---|")
foreach ($record in $targetFrameworkRecords | Sort-Object scope, file) {
    $reportLines.Add("| $(Format-MarkdownCode $record.scope) | $(Format-MarkdownCode $record.file) | $(Format-MarkdownCode (@($record.targetFrameworks) -join ';')) |")
}

$reportLines.Add("")
$reportLines.Add("## Dotnet Tools")
$reportLines.Add("")
$reportLines.Add("| Scope | Tool | Version | File |")
$reportLines.Add("|---|---|---|---|")
foreach ($record in $toolRecords | Sort-Object tool, file) {
    $reportLines.Add("| $(Format-MarkdownCode $record.scope) | $(Format-MarkdownCode $record.tool) | $(Format-MarkdownCode $record.version) | $(Format-MarkdownCode $record.file) |")
}

if ($packageQueryErrors.Count -gt 0) {
    $reportLines.Add("")
    $reportLines.Add("## Package Query Errors")
    $reportLines.Add("")
    foreach ($errorRecord in $packageQueryErrors) {
        $reportLines.Add("- $(Format-MarkdownCode $errorRecord.solution) $(Format-MarkdownCode $errorRecord.kind): $($errorRecord.message)")
    }
}

$reportPath = Join-Path $OutputDirectory "maintenance-report.md"
$jsonReportPath = Join-Path $OutputDirectory "maintenance-report.json"
Set-Content -LiteralPath $reportPath -Value $reportLines -Encoding utf8

$findingValues = @($findings.Values | ForEach-Object { $_ })

$metadata = [ordered]@{
    generatedAt = $generatedAt
    repositoryRoot = $RepositoryRoot
    canCloseResolved = [bool]((-not [bool]$SkipPackageQueries) -and $packageQueryErrors.Count -eq 0)
    findings = $findingValues
    issuePayloads = @($issuePayloads.ToArray())
    packageVersionRecords = @($packageVersionRecords.ToArray())
    targetFrameworkRecords = @($targetFrameworkRecords.ToArray())
    toolRecords = @($toolRecords.ToArray())
    workflowRecords = @($workflowRecords.ToArray())
    packageQueryRows = @($packageQueryRows.ToArray())
    packageQueryErrors = @($packageQueryErrors.ToArray())
}

$metadata | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $jsonReportPath -Encoding utf8

Write-Host "Maintenance report written to $reportPath"
Write-Host "Issue payloads written to $IssueDirectory"
Write-Host "Findings: $($issuePayloads.Count)"
