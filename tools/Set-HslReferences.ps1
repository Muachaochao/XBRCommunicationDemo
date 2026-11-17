param([Parameter(Mandatory=$true)][string]$SourceDirectory)
$ErrorActionPreference='Stop'
$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
$root = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $root 'lib\hsl'
foreach ($name in @('HslCommunication.dll','Newtonsoft.Json.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) { throw "Missing dependency: $name" }
}
New-Item -ItemType Directory -Force -Path $destination | Out-Null
foreach ($name in @('HslCommunication.dll','Newtonsoft.Json.dll')) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $destination $name)
}
Write-Output 'Local references prepared in lib/hsl (excluded from Git). Keep the original library authorization.'
