<#
.SYNOPSIS
    Generates plain-text release notes from conventional commits since the last tag.

.DESCRIPTION
    Finds the latest git tag matching the given prefix, collects commits since
    that tag (filtered to the project directory), parses conventional commit
    messages (subject and body), and outputs categorised plain-text release notes
    suitable for NuGet PackageReleaseNotes (which does not support markdown).

    Commit body text is included as indented detail under each entry to provide
    richer context in the changelog. BREAKING CHANGE footers in the body are
    detected and promoted to the breaking changes section per the Conventional
    Commits specification. Git trailers (Co-authored-by, Signed-off-by, etc.)
    are stripped from the body before inclusion.

    Individual commits can be excluded by adding [skip notes] anywhere in the
    subject or body (useful for build/infra/tooling changes that carry a feat
    or fix type but are not relevant to package consumers).

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

# Collect commit subjects and bodies touching the project directory.
# A unique separator delimits each commit so subject and body can be parsed.
# --no-merges skips merge commits (whose messages are noise like "Merge PR #N")
# but still traverses merged branches so feature/fix commits are included.
$commitSeparator = '---commit-8a3f9b2e---'
$commitArgs = @('log', $range, "--format=${commitSeparator}%n%s%n%b", '--no-merges', '--', $ProjectPath)
$raw = (& git @commitArgs) -join "`n"

if (-not $raw -or $raw.Trim().Length -eq 0) {
    # No commits since last tag – nothing to report.
    exit 0
}

$commitBlocks = $raw -split [regex]::Escape($commitSeparator) | Where-Object { $_.Trim() }

if (-not $commitBlocks -or @($commitBlocks).Count -eq 0) {
    exit 0
}

# Regex to strip common git trailers from the body.
$trailerPattern = '(?m)^(Co-authored-by|Signed-off-by|Reviewed-by|Acked-by|Tested-by|Reported-by|Helped-by|Cc|Change-Id):\s.*$'

# Parse conventional commit subjects into categorised lists.
# Each entry is a hashtable with Description (subject text) and Body (cleaned body text).
$breaking = [System.Collections.Generic.List[hashtable]]::new()
$features = [System.Collections.Generic.List[hashtable]]::new()
$fixes = [System.Collections.Generic.List[hashtable]]::new()
$perf = [System.Collections.Generic.List[hashtable]]::new()

# Types that are not interesting for end-user release notes.
$skipTypes = @('release', 'build', 'ci', 'chore', 'docs', 'style', 'refactor', 'test')

foreach ($block in $commitBlocks) {
    $lines = $block.Trim() -split "`n"
    $subject = $lines[0].Trim()
    $body = ''
    if ($lines.Count -gt 1) {
        $body = ($lines[1..($lines.Count - 1)] -join "`n").Trim()
    }

    # Allow individual commits to opt out of release notes.
    $fullMessage = "$subject`n$body"
    if ($fullMessage -match '\[skip notes\]') {
        continue
    }

    # Match: type(scope)!: description   or   type!: description   or   type: description
    if ($subject -match '^(?<type>\w+)(?:\([^)]*\))?(?<bang>!)?:\s*(?<desc>.+)$') {
        $type = $Matches['type'].ToLowerInvariant()
        $desc = $Matches['desc'].Trim()
        $isBreaking = $Matches['bang'] -eq '!'

        # Detect BREAKING CHANGE footer in body (per Conventional Commits spec).
        if ($body -match '(?m)^BREAKING[- ]CHANGE:\s*(?<bcdesc>.+)') {
            $isBreaking = $true
        }

        # Clean body: strip BREAKING CHANGE footers and git trailers.
        $cleanBody = $body -replace '(?m)^BREAKING[- ]CHANGE:\s*.*$', ''
        $cleanBody = $cleanBody -replace $trailerPattern, ''
        # Collapse runs of blank lines and trim.
        $cleanBody = ($cleanBody -replace '(\r?\n){3,}', "`n`n").Trim()

        $entry = @{ Description = $desc; Body = $cleanBody }

        if ($isBreaking) {
            $breaking.Add($entry)
        }
        elseif ($type -eq 'feat') {
            $features.Add($entry)
        }
        elseif ($type -eq 'fix') {
            $fixes.Add($entry)
        }
        elseif ($type -eq 'perf') {
            $perf.Add($entry)
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

function Add-Section([string]$heading, [System.Collections.Generic.List[hashtable]]$items) {
    if ($items.Count -eq 0) { return }
    $sections.Add($heading)
    foreach ($item in $items) {
        $sections.Add("- $($item.Description)")
        if ($item.Body) {
            $bodyLines = $item.Body -split "`n"
            foreach ($line in $bodyLines) {
                $sections.Add("  $line")
            }
        }
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
