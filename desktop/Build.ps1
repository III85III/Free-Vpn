$ErrorActionPreference = 'Stop'
$root=$PSScriptRoot
$output=Join-Path $root 'output'
New-Item -ItemType Directory -Path $output -Force | Out-Null
foreach($directory in @('interface','assets')) { Copy-Item -LiteralPath "$root/$directory" -Destination $output -Recurse -Force }
foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')) { Copy-Item -LiteralPath "$root/lib/webview2/$name" -Destination $output -Force }
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$refs=@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Net.Http.dll','System.Web.Extensions.dll','Accessibility.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.ServiceProcess.dll','System.Security.dll') | ForEach-Object { "/reference:$_" }
$refs+=@("/reference:$output/Microsoft.Web.WebView2.Core.dll","/reference:$output/Microsoft.Web.WebView2.WinForms.dll")
$sources=@('Onyx.Vpn.cs','OnyxWindow.cs','SignInForm.cs','VerificationForm.cs','Updates.cs','UiStyle.cs') | ForEach-Object { Join-Path $root $_ }
& $compiler /nologo /codepage:65001 /utf8output /platform:x64 /target:winexe /optimize+ "/out:$output/onyx_vpn.exe" "/win32icon:$root/assets/brand/onyx.ico" "/win32manifest:$root/app.manifest" "/resource:$root/assets/brand/infinity_loop.png,infinity_loop" "/resource:$root/interface/icon.png,brand_logo" @refs @sources
if($LASTEXITCODE){throw 'ONYX compilation failed.'}
Copy-Item -LiteralPath "$root/runtime.config" -Destination "$output/onyx_vpn.exe.config" -Force
Write-Output "$output/onyx_vpn.exe"
