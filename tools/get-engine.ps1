param([Parameter(Mandatory=$true)][string]$EngineDirectory,[Parameter(Mandatory=$true)][string]$SourceArchive)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$destination=Join-Path $root 'vendor\engine'
$expected=Get-Content -LiteralPath "$root\third_party\engine\manifest.json" -Raw|ConvertFrom-Json
foreach($name in 'firefox-vpn-core.exe','firefox-vpn-tun.exe','firefox-vpn-tun.exe.sig'){
 if((Get-FileHash -LiteralPath (Join-Path $EngineDirectory $name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected.$name){throw "Unexpected original engine: $name"}
}
if(-not(Test-Path -LiteralPath $SourceArchive -PathType Leaf)){throw 'The upstream source archive is required.'}
$sourceInfo=Get-Content -LiteralPath "$root\third_party\engine\source.json" -Raw|ConvertFrom-Json
if((Get-FileHash -LiteralPath $SourceArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sourceInfo.archive_sha256){throw 'Unexpected upstream source archive.'}
New-Item -ItemType Directory -Path $destination -Force|Out-Null
foreach($name in 'firefox-vpn-core.exe','firefox-vpn-tun.exe','firefox-vpn-tun.exe.sig'){Copy-Item -LiteralPath (Join-Path $EngineDirectory $name) -Destination $destination -Force}
Copy-Item -LiteralPath $SourceArchive -Destination "$destination\upstream-source.zip" -Force
Write-Output 'Original engine components verified and cached with upstream source.'
