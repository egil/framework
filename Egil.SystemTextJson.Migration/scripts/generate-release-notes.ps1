<#
.SYNOPSIS
    Generates plain-text release notes from conventional commits since the last tag.

.DESCRIPTION
    Finds the latest git tag matching the given prefix, collects commits since
    that tag (filtered to the project directory), parses conventional commit
    messages, and outputs categorised plain-text release notes suitable for
    NuGet PackageReleaseNotes (which does not support markdown).

.PARAMETER TagPrefix
    Tag name prefix used to find the latest release tag.
    Default: "egil-systemtextjson-migration/v"

.PARAMETER ProjectPath
    Project directory path relative to the repository root.
    Only commits touching files under this path are included.
    Default: "Egil.SystemTextJson.Migration/"

.EXAMPLE
    ./scripts/generate-release-notes.ps1
    ./scripts/generate-release-notes.ps1 -TagPrefix "my-project/v" -ProjectPath "MyProject/"
#>
[CmdletBinding()]
param(
    [string]$TagPrefix = "egil-systemtextjson-migration/v",
    [string]$ProjectPath = "Egil.SystemTextJson.Migration/"
)

$ErrorActionPreference = 'Stop'

# Verify we are inside a git repository.
$gitDir = git rev-parse --git-dir 2>&1
if ($LASTEXITCODE -ne 0) {
    # Not a git repo – silently produce no output so the build is not broken.
    exit 0
}

# Find the latest tag that matches the prefix.
$latestTag = git tag -l "${TagPrefix}*" --sort=-v:refname | Select-Object -First 1

if ($latestTag) {
    $range = "${latestTag}..HEAD"
}
else {
    $range = "HEAD"
}

# Collect one-line commit subjects touching the project directory.
# --no-merges skips merge commits (whose messages are noise like "Merge PR #N")
# but still traverses merged branches so feature/fix commits are included.
$commitArgs = @('log', $range, '--format=%s', '--no-merges', '--', $ProjectPath)
$subjects = & git @commitArgs

if (-not $subjects -or $subjects.Count -eq 0) {
    # No commits since last tag – nothing to report.
    exit 0
}

# Parse conventional commit subjects into categorised lists.
$breaking = [System.Collections.Generic.List[string]]::new()
$features = [System.Collections.Generic.List[string]]::new()
$fixes = [System.Collections.Generic.List[string]]::new()
$perf = [System.Collections.Generic.List[string]]::new()

# Types that are not interesting for end-user release notes.
$skipTypes = @('release', 'build', 'ci', 'chore', 'docs', 'style', 'refactor', 'test')

foreach ($subject in $subjects) {
    # Match: type(scope)!: description   or   type!: description   or   type: description
    if ($subject -match '^(?<type>\w+)(?:\([^)]*\))?(?<bang>!)?:\s*(?<desc>.+)$') {
        $type = $Matches['type'].ToLowerInvariant()
        $desc = $Matches['desc'].Trim()
        $isBreaking = $Matches['bang'] -eq '!'

        if ($isBreaking) {
            $breaking.Add($desc)
        }
        elseif ($type -eq 'feat') {
            $features.Add($desc)
        }
        elseif ($type -eq 'fix') {
            $fixes.Add($desc)
        }
        elseif ($type -eq 'perf') {
            $perf.Add($desc)
        }
        elseif ($type -in $skipTypes) {
            continue
        }
        # Unknown types are silently skipped to keep notes clean.
    }
    # Non-conventional commits are silently skipped.
}

# Build plain-text output (no markdown – NuGet does not render it).
$sections = [System.Collections.Generic.List[string]]::new()

function Add-Section([string]$heading, [System.Collections.Generic.List[string]]$items) {
    if ($items.Count -eq 0) { return }
    $sections.Add($heading)
    foreach ($item in $items) {
        $sections.Add("- $item")
    }
}

Add-Section "BREAKING CHANGES:" $breaking
Add-Section "New Features:" $features
Add-Section "Bug Fixes:" $fixes
Add-Section "Performance:" $perf

if ($sections.Count -eq 0) {
    exit 0
}

# Write to stdout – MSBuild ConsoleToMSBuild will capture this.
$sections -join "`n"
