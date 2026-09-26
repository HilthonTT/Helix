<#
.SYNOPSIS
    Runs the test suites.

.DESCRIPTION
    Runs Application, Infrastructure and Architecture by default. On macOS only the Application
    suite can run, since the other two target the Windows head.

.EXAMPLE
    ./scripts/test.ps1
.EXAMPLE
    ./scripts/test.ps1 -Suite Application -Filter CreateDriveTests
.EXAMPLE
    ./scripts/test.ps1 -Configuration Release -NoBuild -Trx
#>
[CmdletBinding()]
param(
    [ValidateSet('All', 'Application', 'Infrastructure', 'Architecture')]
    [string[]]$Suite = 'All',

    # Matched against the fully qualified test name, as --filter "FullyQualifiedName~<Filter>".
    [string]$Filter,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$NoBuild,

    # Writes a .trx per suite to TestResults/, like CI does.
    [switch]$Trx
)

. (Join-Path $PSScriptRoot '_common.ps1')

if ($Suite -contains 'All') {
    $Suite = @($TestProjects.Keys)
}

if (-not (Test-IsWindowsHost)) {
    $skipped = @($Suite | Where-Object { $_ -ne 'Application' })
    if ($skipped.Count -gt 0) {
        Write-Warning "Skipping $($skipped -join ', '): these suites target the Windows head."
    }
    $Suite = @($Suite | Where-Object { $_ -eq 'Application' })
}

$failed = @()
foreach ($name in $Suite) {
    Write-Step "Testing $name ($Configuration)"

    $arguments = @('test', $TestProjects[$name], '--configuration', $Configuration)
    if ($NoBuild) { $arguments += '--no-build' }
    if ($Filter) { $arguments += '--filter', "FullyQualifiedName~$Filter" }
    if ($Trx) {
        $arguments += '--logger', "trx;LogFileName=$($name.ToLowerInvariant()).trx"
        $arguments += '--results-directory', (Join-Path $RepoRoot 'TestResults')
    }

    # Keep going after a failing suite so one run reports all of them.
    try { Invoke-Dotnet $arguments }
    catch { $failed += $name }
}

if ($failed.Count -gt 0) {
    throw "Failing suites: $($failed -join ', ')"
}

Write-Host ''
Write-Host "All suites passed: $($Suite -join ', ')" -ForegroundColor Green
