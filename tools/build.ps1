param([string]$OutputDirectory,[string]$DesktopSource)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $root 'dist\III85III VPN'}
if(-not $DesktopSource -and (Test-Path -LiteralPath "$root\native\III85III.Vpn.cs")){$DesktopSource="$root\native\III85III.Vpn.cs"}
New-Item -ItemType Directory -Path $out,"$out\runtime","$out\src","$out\licenses","$out\source\src","$out\source\native","$out\source\tools","$out\source\test" -Force | Out-Null
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$iconFile=Join-Path $root 'native\assets\brand\iii85iii-luxury.ico'
if(-not(Test-Path -LiteralPath $iconFile)){throw 'The original III85III brand icon is missing.'}
$refs=@('/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Net.Http.dll','/reference:System.Web.Extensions.dll','/reference:System.Security.dll')
foreach($target in @(@{name='III85III VPN';file='App.cs';kind='winexe'},@{name='III85III Tools';file='Tools.cs';kind='exe'},@{name='III85III Route Host';file='RouteHost.cs';kind='winexe'})){
    $inputs=@("$root\native\$($target.file)")
    if($target.file -eq 'App.cs'){
        if($DesktopSource){$inputs=@($DesktopSource,(Join-Path (Split-Path -Parent $DesktopSource) 'DesktopFeatures.cs'),(Join-Path (Split-Path -Parent $DesktopSource) 'VerificationForm.cs'))}
        else{$inputs+="$root\native\Ui.cs"}
    }
    & $compiler /nologo /codepage:65001 /utf8output /platform:x64 /optimize+ "/target:$($target.kind)" "/out:$out\$($target.name).exe" "/win32icon:$iconFile" "/win32manifest:$root\native\app.manifest" @refs @inputs
    if($LASTEXITCODE -ne 0){throw "Compilation failed: $($target.name)"}
    Copy-Item -LiteralPath "$root\native\runtime.config" -Destination "$out\$($target.name).exe.config" -Force
}
$nodeCommand=(Get-Command node -ErrorAction Stop).Source
Copy-Item -LiteralPath $nodeCommand -Destination "$out\runtime\node.exe" -Force
$nodeLicense=Join-Path (Split-Path -Parent $nodeCommand) 'LICENSE'
if(Test-Path -LiteralPath $nodeLicense){Copy-Item -LiteralPath $nodeLicense -Destination "$out\licenses\Node-LICENSE.txt" -Force}else{
    $nodeVersion=& $nodeCommand -p 'process.version'
    if($nodeVersion -notmatch '^v\d+\.\d+\.\d+$'){throw 'Unexpected runtime version.'}
    Invoke-WebRequest -Uri "https://raw.githubusercontent.com/nodejs/node/$nodeVersion/LICENSE" -OutFile "$out\licenses\Node-LICENSE.txt"
}
$release=Get-ChildItem -LiteralPath "$root\vendor" -Directory | Where-Object {$_.Name -like 'sing-box-v*'} | Select-Object -First 1
$router=Get-ChildItem -LiteralPath $release.FullName -Filter sing-box.exe -Recurse -File | Select-Object -First 1
if(-not $router){throw 'Run get-router.ps1 first.'}
Copy-Item -LiteralPath $router.FullName -Destination "$out\runtime\sing-box.exe" -Force
Get-ChildItem -LiteralPath $router.DirectoryName -Filter '*.dll' -File | Copy-Item -Destination "$out\runtime" -Force
$routerLicense=Join-Path $router.DirectoryName 'LICENSE'
if(Test-Path -LiteralPath $routerLicense){Copy-Item -LiteralPath $routerLicense -Destination "$out\licenses\sing-box-LICENSE.txt" -Force}
Get-ChildItem -LiteralPath "$root\src" -Filter '*.cjs' | Copy-Item -Destination "$out\src" -Force
Get-ChildItem -LiteralPath "$root\native" -File | Copy-Item -Destination "$out\source\native" -Force
if($DesktopSource){Copy-Item -LiteralPath $DesktopSource,(Join-Path (Split-Path -Parent $DesktopSource) 'DesktopFeatures.cs') -Destination "$out\source\native" -Force}
Copy-Item -LiteralPath "$root\native\assets" -Destination "$out\source\native" -Recurse -Force
Copy-Item -LiteralPath "$root\native\assets" -Destination $out -Recurse -Force
if(Test-Path -LiteralPath "$root\docs"){Copy-Item -LiteralPath "$root\docs" -Destination $out -Recurse -Force;Copy-Item -LiteralPath "$root\docs" -Destination "$out\source" -Recurse -Force}
Get-ChildItem -LiteralPath "$root\tools" -File | Copy-Item -Destination "$out\source\tools" -Force
Get-ChildItem -LiteralPath "$root\test" -File | Copy-Item -Destination "$out\source\test" -Force
Get-ChildItem -LiteralPath "$root\src" -Filter '*.cjs' -File | Copy-Item -Destination "$out\source\src" -Force
foreach($document in 'package.json','README.md','LICENSE'){
    if(Test-Path -LiteralPath "$root\$document"){Copy-Item -LiteralPath "$root\$document" -Destination "$out\source" -Force}
}
New-Item -ItemType Directory -Path "$out\validation" -Force | Out-Null
Get-ChildItem -LiteralPath "$root\test-results" -Filter 'smoke.json' -File | Copy-Item -Destination "$out\validation" -Force
Get-ChildItem -LiteralPath "$root\vendor\licenses" -File | Copy-Item -Destination "$out\licenses" -Force
Copy-Item -LiteralPath "$root\docs\guide.txt" -Destination "$out\START-HERE.txt" -Force
Copy-Item -LiteralPath "$root\docs\guide.html" -Destination "$out\START-HERE.html" -Force
Copy-Item -LiteralPath "$root\THIRD-PARTY-NOTICES.txt" -Destination $out -Force
foreach($document in 'README.md','LICENSE'){
    if(Test-Path -LiteralPath "$root\$document"){Copy-Item -LiteralPath "$root\$document" -Destination $out -Force}
}
Copy-Item -LiteralPath "$root\vendor\router-provenance.json" -Destination "$out\licenses" -Force
$hashes=[ordered]@{}
foreach($file in Get-ChildItem -LiteralPath $out -File -Recurse | Where-Object {$_.Extension -eq '.exe'}){$name=$file.FullName.Substring($out.Length+1);$hashes[$name]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
$hashes | ConvertTo-Json | Set-Content -LiteralPath "$out\component-hashes.json" -Encoding UTF8
Write-Output "Built independent client: $out\III85III VPN.exe"
