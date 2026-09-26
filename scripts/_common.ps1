# Shared by every script in this folder; dot-source it, do not run it.
# Written for Windows PowerShell 5.1 as well as PowerShell 7, so no ternaries, no && and no
# $IsWindows without a guard (5.1 does not define it).

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:Solution = Join-Path $RepoRoot 'Helix.slnx'
$script:AppProject = Join-Path $RepoRoot 'src/Helix.App/Helix.App.csproj'
$script:InfrastructureProject = Join-Path $RepoRoot 'src/Helix.Infrastructure'
$script:WindowsFramework = 'net10.0-windows10.0.19041.0'
$script:MacFramework = 'net10.0-maccatalyst'

$script:TestProjects = [ordered]@{
    Application    = Join-Path $RepoRoot 'tests/Application.UnitTests/Application.UnitTests.csproj'
    Infrastructure = Join-Path $RepoRoot 'tests/Infrastructure.UnitTests/Infrastructure.UnitTests.csproj'
    Architecture   = Join-Path $RepoRoot 'tests/ArchitectureTests/ArchitectureTests.csproj'
}

# Infrastructure.UnitTests and ArchitectureTests target the Windows head, so on a Mac only the
# Application suite and the app project on its own can be built.
function Test-IsWindowsHost {
    if ($PSVersionTable.PSEdition -eq 'Desktop') { return $true }
    return [bool]$IsWindows
}

$env:DOTNET_NOLOGO = 'true'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 'true'

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

# Runs dotnet and fails the script on a non-zero exit code, which a native command does not do
# on its own under $ErrorActionPreference = 'Stop'.
# Deliberately not an advanced function: dotnet's own switches (-f, -o, -c) would otherwise be
# bound against PowerShell's common parameters. Pass the arguments as one array.
function Invoke-Dotnet([string[]]$Arguments) {
    Write-Host "dotnet $($Arguments -join ' ')" -ForegroundColor DarkGray
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}
