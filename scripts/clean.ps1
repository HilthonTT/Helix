<#
.SYNOPSIS
    Deletes every bin and obj folder under src/ and tests/.

.DESCRIPTION
    The fix for a build that goes on reporting the previous version, or for any other stale
    generated file under obj.

.EXAMPLE
    ./scripts/clean.ps1
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param()

. (Join-Path $PSScriptRoot '_common.ps1')

Write-Step 'Removing bin and obj folders'

$roots = @('src', 'tests') | ForEach-Object { Join-Path $RepoRoot $_ }
$folders = Get-ChildItem -Path $roots -Directory -Recurse -Include 'bin', 'obj' |
    Where-Object { Test-Path (Join-Path $_.Parent.FullName '*.csproj') }

foreach ($folder in $folders) {
    # A folder nested inside one already removed is gone by the time it comes up.
    if (-not (Test-Path -LiteralPath $folder.FullName)) { continue }
    if ($PSCmdlet.ShouldProcess($folder.FullName, 'Remove')) {
        Remove-Item -LiteralPath $folder.FullName -Recurse -Force
        Write-Host "  removed $($folder.FullName.Substring($RepoRoot.Length + 1))"
    }
}

if (@($folders).Count -eq 0) {
    Write-Host '  nothing to remove'
}
