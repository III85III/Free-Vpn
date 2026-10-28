param([string]$AppDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$app=if($AppDirectory){[IO.Path]::GetFullPath($AppDirectory)}else{Join-Path $root 'dist\III85III VPN 2.1 Preview'}
if(-not(Test-Path -LiteralPath "$app\III85III VPN.exe")){throw 'Build the preview first.'}
$forbidden=@('account.dpapi','accounts.dpapi','settings.json','config.json','route.json','route-state.json','route-stop','local-api.token','ui-prefs.json','PROJECT.md','RESUME-NOTES.md','FEATURE-PLAN.md','upstream-source.zip','firefox-vpn-core.exe','firefox-vpn-tun.exe')
$bad=Get-ChildItem -LiteralPath $app -Recurse -File | Where-Object {$_.Name -in $forbidden -or $_.Extension -in '.dpapi','.token','.log','.pdb'}
if($bad){throw ('Private/obsolete artifact in package: '+(($bad.Name|Sort-Object -Unique)-join ', '))}
$zip=Join-Path $root 'III85III-VPN-v2.1.0-preview.2-windows-x64.zip'
Compress-Archive -LiteralPath $app -DestinationPath $zip -CompressionLevel Optimal -Force
$hash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath "$zip.sha256" -Encoding ASCII
Write-Output "Package: $zip"
Write-Output "SHA256: $hash"
