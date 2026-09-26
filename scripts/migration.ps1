<#
.SYNOPSIS
    Adds, removes or lists EF Core migrations.

.DESCRIPTION
    Migrations live in Helix.Infrastructure but the EF tools are referenced from Helix.App, so
    every command runs with the App as the startup project and the host's target framework.
    dotnet-ef comes from the local tool
    manifest in .config/ and is restored first.

.EXAMPLE
    ./scripts/migration.ps1 add AddDriveNotes
.EXAMPLE
    ./scripts/migration.ps1 remove
.EXAMPLE
    ./scripts/migration.ps1 list
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('add', 'remove', 'list')]
    [string]$Action,

    [Parameter(Position = 1)]
    [string]$Name
)

. (Join-Path $PSScriptRoot '_common.ps1')

if ($Action -eq 'add' -and -not $Name) {
    throw 'A migration name is required: ./scripts/migration.ps1 add <Name>'
}

Push-Location $RepoRoot
try {
    Invoke-Dotnet @('tool', 'restore')

    $arguments = @('ef', 'migrations', $Action)
    if ($Action -eq 'add') { $arguments += $Name }
    $arguments += '--project', $InfrastructureProject, '--startup-project', $AppProject

    # The projects are multi-targeted, and dotnet ef refuses to guess which head to load.
    if (Test-IsWindowsHost) { $framework = $WindowsFramework } else { $framework = $MacFramework }
    $arguments += '--framework', $framework

    Write-Step "dotnet ef migrations $Action $Name"
    Invoke-Dotnet $arguments
}
finally {
    Pop-Location
}
