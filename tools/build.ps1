param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $root '.build\app'}
if(Test-Path -LiteralPath $out){throw 'Use an empty output directory to avoid mixing engine versions.'}
$engine=Join-Path $root 'vendor\engine'
$expected=Get-Content -LiteralPath "$root\third_party\engine\manifest.json" -Raw|ConvertFrom-Json
foreach($name in 'firefox-vpn-core.exe','firefox-vpn-tun.exe','firefox-vpn-tun.exe.sig'){
 if(-not(Test-Path -LiteralPath "$engine\$name")){throw 'Run tools/get-engine.ps1 with the original engine and source archive first.'}
 if((Get-FileHash -LiteralPath "$engine\$name" -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected.$name){throw "Original engine hash mismatch: $name"}
}
if(-not(Test-Path -LiteralPath "$engine\upstream-source.zip")){throw 'The upstream source archive is required.'}
$sourceInfo=Get-Content -LiteralPath "$root\third_party\engine\source.json" -Raw|ConvertFrom-Json
if((Get-FileHash -LiteralPath "$engine\upstream-source.zip" -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sourceInfo.archive_sha256){throw 'Unexpected upstream source archive.'}
New-Item -ItemType Directory -Path $out,"$out\core","$out\licenses","$out\source\native","$out\source\tools","$out\source\third_party" -Force|Out-Null
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs=@('/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Net.Http.dll','/reference:System.Web.Extensions.dll')
& $compiler /nologo /codepage:65001 /utf8output /platform:x64 /target:winexe /optimize+ "/out:$out\III85III VPN.exe" "/win32icon:$root\native\assets\brand\iii85iii-luxury.ico" "/win32manifest:$root\native\app.manifest" @refs "$root\native\III85III.Vpn.cs" "$root\native\SignInForm.cs" "$root\native\VerificationForm.cs"
if($LASTEXITCODE -ne 0){throw 'Application compilation failed.'}
foreach($name in 'firefox-vpn-core.exe','firefox-vpn-tun.exe','firefox-vpn-tun.exe.sig'){Copy-Item -LiteralPath "$engine\$name" -Destination "$out\core"}
Copy-Item -LiteralPath "$root\third_party\engine\manifest.json" -Destination "$out\core\manifest.json"
Get-ChildItem -LiteralPath "$root\licenses" -File|Copy-Item -Destination "$out\licenses"
Copy-Item -LiteralPath "$root\native\assets\fonts\OFL.txt" -Destination "$out\licenses\Lato-OFL.txt"
Copy-Item -LiteralPath "$root\native\assets" -Destination $out -Recurse
Copy-Item -LiteralPath "$root\docs" -Destination $out -Recurse
Copy-Item -LiteralPath "$root\docs" -Destination "$out\source" -Recurse
Get-ChildItem -LiteralPath "$root\native" -File|Copy-Item -Destination "$out\source\native"
Copy-Item -LiteralPath "$root\native\assets" -Destination "$out\source\native" -Recurse
Get-ChildItem -LiteralPath "$root\tools" -File|Copy-Item -Destination "$out\source\tools"
Copy-Item -LiteralPath "$root\third_party\engine" -Destination "$out\source\third_party" -Recurse
Copy-Item -LiteralPath "$engine\upstream-source.zip" -Destination "$out\source"
Copy-Item -LiteralPath "$root\third_party\engine\PROVENANCE.txt" -Destination "$out\source\PROVENANCE.txt"
Copy-Item -LiteralPath "$root\.github" -Destination "$out\source" -Recurse
Copy-Item -LiteralPath "$root\licenses" -Destination "$out\source" -Recurse
Write-Output "Built III85III VPN with the original unchanged engine: $out"
