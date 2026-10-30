param([string]$AppDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$app=if($AppDirectory){[IO.Path]::GetFullPath($AppDirectory)}else{Join-Path $root '.build\app'}
if(-not(Test-Path -LiteralPath "$app\III85III VPN.exe")){throw 'Build the application first.'}
$forbidden=@('account.dpapi','accounts.dpapi','settings.json','config.json','route.json','route-state.json','route-stop','local-api.token','ui-prefs.json','required-update.txt','PROJECT.md','RESUME-NOTES.md','FEATURE-PLAN.md','accounts.json','account.json','credentials.json','cookies.json')
$bad=Get-ChildItem -LiteralPath $app -Recurse -File | Where-Object {$_.Name -in $forbidden -or $_.Extension -in '.dpapi','.token','.log','.pdb'}
if($bad){throw ('Private/obsolete artifact in package: '+(($bad.Name|Sort-Object -Unique)-join ', '))}
$version=(Get-Content -LiteralPath "$root\tools\project.json" -Raw|ConvertFrom-Json).version
$packages=Join-Path $root '.build\packages'
New-Item -ItemType Directory -Path $packages -Force|Out-Null
$zip=Join-Path $packages ("III85III-VPN-v$version-windows-x64.zip")
Compress-Archive -LiteralPath $app -DestinationPath $zip -CompressionLevel Optimal -Force
$hash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath "$zip.sha256" -Encoding ASCII
Write-Output "Package: $zip"
Write-Output "SHA256: $hash"
