$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$folder=Join-Path $root 'vendor\licenses'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
Invoke-WebRequest -Uri 'https://www.gnu.org/licenses/gpl-3.0.txt' -OutFile "$folder\GPL-3.0.txt"
$info=Get-Content -LiteralPath "$root\vendor\router-provenance.json" -Raw | ConvertFrom-Json
if($info.release -notmatch '^v\d+\.\d+\.\d+$'){throw 'Unexpected router release.'}
Invoke-WebRequest -Uri "https://codeload.github.com/SagerNet/sing-box/zip/refs/tags/$($info.release)" -OutFile "$folder\sing-box-source-$($info.release).zip"
Write-Output 'Neutral routing license and release source downloaded.'
