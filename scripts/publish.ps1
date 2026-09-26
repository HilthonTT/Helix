<#
.SYNOPSIS
    Publishes the Windows build the way the release workflow does.

.DESCRIPTION
    Writes a self-contained build per runtime to publish/<rid>, and with -Zip archives it as
    artifacts/Helix-<tag>-<rid>.zip, the name the in-app updater selects on. The archives are
    not signed; release signing happens only in the release workflow.

.EXAMPLE
    ./scripts/publish.ps1
.EXAMPLE
    ./scripts/publish.ps1 -Runtime win-x64, win-arm64 -Zip -Tag v2.2.6
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string[]]$Runtime = 'win-x64',

    [switch]$Zip,

    # Used in the archive name. Defaults to the latest tag reachable from HEAD.
    [string]$Tag
)

. (Join-Path $PSScriptRoot '_common.ps1')

if (-not (Test-IsWindowsHost)) {
    throw 'publish.ps1 builds the Windows head; the macOS archive is produced by the release workflow.'
}

if ($Zip -and -not $Tag) {
    $Tag = (& git -C $RepoRoot describe --tags --abbrev=0 2>$null)
    if (-not $Tag) { $Tag = 'local' }
}

foreach ($rid in $Runtime) {
    $output = Join-Path $RepoRoot "publish/$rid"

    Write-Step "Publishing $rid"
    if (Test-Path $output) { Remove-Item -LiteralPath $output -Recurse -Force }

    Invoke-Dotnet @(
        'publish', $AppProject,
        '-f', $WindowsFramework,
        '-c', 'Release',
        "-p:RuntimeIdentifierOverride=$rid",
        '-p:AppxPackageSigningEnabled=false',
        '-o', $output)

    if ($Zip) {
        $artifacts = Join-Path $RepoRoot 'artifacts'
        New-Item -ItemType Directory -Force $artifacts | Out-Null
        $archive = Join-Path $artifacts "Helix-$Tag-$rid.zip"

        Write-Step "Archiving $(Split-Path -Leaf $archive)"
        if (Test-Path $archive) { Remove-Item -LiteralPath $archive -Force }
        Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive
    }
}

Write-Host ''
Write-Host "Published to $(Join-Path $RepoRoot 'publish')" -ForegroundColor Green
