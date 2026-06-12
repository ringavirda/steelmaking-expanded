param(
    [Parameter(Mandatory = $true)][string]$Dest,
    [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)][string[]]$Mods
)

$ErrorActionPreference = 'Stop'

if (Test-Path $Dest) { Remove-Item -Recurse -Force $Dest }
New-Item -ItemType Directory -Force -Path $Dest | Out-Null

foreach ($entry in $Mods) {
    $name, $src = $entry -split '=', 2
    if (-not (Test-Path $src)) { throw "Mod source not found: $src" }
    Copy-Item -Recurse -Force -Path $src -Destination (Join-Path $Dest $name)
    Write-Host "Staged '$name' from $src"
}

Write-Host "Staged $($Mods.Count) mod(s) into $Dest"
