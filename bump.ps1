<#
.SYNOPSIS
    Sets a new PxeVice version.

.DESCRIPTION
    Writes the version to PxeVice.wxs with a new product code (the upgrade code stays the same),
    and src/AssemblyInfo.cs, and adds an empty dated section for it at the top of CHANGELOG.md.
    It refuses a version that is not higher than the one in PxeVice.wxs.

.PARAMETER Version
    The new version, as major.minor.patch.

.EXAMPLE
    .\bump.ps1 -Version 1.2.0
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

$utf8 = New-Object System.Text.UTF8Encoding -ArgumentList $false

function Update-TextFile {
    param(
        [string]$Path,
        [string[]]$Pattern,
        [string[]]$Replacement
    )
    $full = Join-Path -Path $PSScriptRoot -ChildPath $Path
    $text = [System.IO.File]::ReadAllText($full)
    for ($i = 0; $i -lt $Pattern.Count; $i++) {
        $regex = New-Object System.Text.RegularExpressions.Regex -ArgumentList $Pattern[$i], 'Multiline'
        if (-not $regex.IsMatch($text)) {
            throw "$Path has no match for $($Pattern[$i]). Check it by hand."
        }
        $text = $regex.Replace($text, $Replacement[$i], 1)
    }
    [System.IO.File]::WriteAllText($full, $text, $script:utf8)
}

$wxsText = [System.IO.File]::ReadAllText((Join-Path -Path $PSScriptRoot -ChildPath 'PxeVice.wxs'))
if ($wxsText -notmatch '(?m)^[ \t]*Version="([^"]+)"') {
    throw 'No Version was found in PxeVice.wxs.'
}
$current = $Matches[1]
if ([version]$Version -le [version]$current) {
    throw "$Version is not higher than the current version $current."
}
$changelog = [System.IO.File]::ReadAllText((Join-Path -Path $PSScriptRoot -ChildPath 'CHANGELOG.md'))
if ($changelog -match ('(?m)^## PxeVice ' + [regex]::Escape($Version) + '\s*$')) {
    throw "CHANGELOG.md already has a section for $Version."
}

$guid = [guid]::NewGuid().ToString().ToUpperInvariant()
$date = (Get-Date).ToString('d MMMM yyyy', [System.Globalization.CultureInfo]::InvariantCulture)

if ($PSCmdlet.ShouldProcess('PxeVice.wxs, src/AssemblyInfo.cs, CHANGELOG.md', "Set version $Version")) {
    Update-TextFile -Path 'PxeVice.wxs' `
        -Pattern '<Product Id="[^"]*"', '^([ \t]*)Version="[^"]*"', 'DefaultVersion="[^"]*"' `
        -Replacement "<Product Id=`"$guid`"", "`${1}Version=`"$Version`"", "DefaultVersion=`"$Version.0`""
    Update-TextFile -Path 'src/AssemblyInfo.cs' `
        -Pattern 'AssemblyVersion\("[^"]*"\)', 'AssemblyFileVersion\("[^"]*"\)', 'AssemblyInformationalVersion\("[^"]*"\)' `
        -Replacement "AssemblyVersion(`"$Version.0`")", "AssemblyFileVersion(`"$Version.0`")", "AssemblyInformationalVersion(`"$Version`")"

    $newline = "`n"
    if ($changelog.Contains("`r`n")) {
        $newline = "`r`n"
    }
    $section = "## PxeVice $Version$newline$newline$date.$newline$newline"
    Update-TextFile -Path 'CHANGELOG.md' -Pattern '^## ' -Replacement ($section.Replace('$', '$$') + '## ')

    Write-Output "PxeVice $current -> $Version, product code {$guid}"
    Write-Output 'Changed PxeVice.wxs, src/AssemblyInfo.cs and CHANGELOG.md.'
    Write-Output 'Write the changes in CHANGELOG.md, then build, test and commit.'
}
