<#
.SYNOPSIS
    Builds Helix.

.DESCRIPTION
    On Windows, builds the whole solution. On macOS, builds the app project on its own, because
    the solution also holds test projects that target the Windows head.

.EXAMPLE
    ./scripts/build.ps1
.EXAMPLE
    ./scripts/build.ps1 -Configuration Release -Clean
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    # Deletes every bin and obj folder first (see clean.ps1).
    [switch]$Clean,

    [switch]$NoRestore
)

. (Join-Path $PSScriptRoot '_common.ps1')

if ($Clean) {
    & (Join-Path $PSScriptRoot 'clean.ps1')
}

if (Test-IsWindowsHost) {
    $target = $Solution
}
else {
    $target = $AppProject
}

Write-Step "Building $(Split-Path -Leaf $target) ($Configuration)"

$arguments = @('build', $target, '--configuration', $Configuration)
if ($NoRestore) { $arguments += '--no-restore' }
if (-not (Test-IsWindowsHost)) {
    $arguments += '-p:EnableCodeSigning=false', '-p:_RequireCodeSigning=false'
}

Invoke-Dotnet $arguments
