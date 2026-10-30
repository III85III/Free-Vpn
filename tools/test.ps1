$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'test-results'
New-Item -ItemType Directory -Force -Path $out|Out-Null
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs=@('/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Net.Http.dll','/reference:System.Web.Extensions.dll','/reference:Accessibility.dll')
foreach($suite in @(@{file='Tests.cs';main='III85III.Vpn.Tests';name='CoreTests'},@{file='ConnectionTests.cs';main='III85III.Vpn.ConnectionTests';name='ConnectionTests'},@{file='LayoutTests.cs';main='III85III.Vpn.LayoutTests';name='LayoutTests'},@{file='LatencyTests.cs';main='III85III.Vpn.LatencyTests';name='LatencyTests'},@{file='SignInTests.cs';main='III85III.Vpn.SignInTests';name='SignInTests'},@{file='VerificationTests.cs';main='III85III.Vpn.VerificationTests';name='VerificationTests'},@{file='UpdateTests.cs';main='III85III.Vpn.UpdateTests';name='UpdateTests'},@{file='RequiredUpdateTests.cs';main='III85III.Vpn.RequiredUpdateTests';name='RequiredUpdateTests'},@{file='PollingTests.cs';main='III85III.Vpn.PollingTests';name='PollingTests'},@{file='KeyboardTests.cs';main='III85III.Vpn.KeyboardTests';name='KeyboardTests'})){
 & $compiler /nologo /codepage:65001 /utf8output /target:exe /platform:x64 "/main:$($suite.main)" "/out:$out\$($suite.name).exe" @refs "$root\native\III85III.Vpn.cs" "$root\native\SignInForm.cs" "$root\native\VerificationForm.cs" "$root\native\Updates.cs" "$root\native\$($suite.file)"
 if($LASTEXITCODE -ne 0){throw 'Native test compilation failed.'}
 & "$out\$($suite.name).exe" | Tee-Object -FilePath "$out\$($suite.name).txt"
 if($LASTEXITCODE -ne 0){throw 'Native test failed.'}
}
& $compiler /nologo /codepage:65001 /utf8output /target:exe /platform:x64 "/out:$out\InstallerTests.exe" /reference:System.dll /reference:System.Core.dll /reference:System.IO.Compression.dll "$root\native\InstallPackage.cs" "$root\native\InstallerTests.cs"
if($LASTEXITCODE -ne 0){throw 'Installer test compilation failed.'}
& "$out\InstallerTests.exe" | Tee-Object -FilePath "$out\InstallerTests.txt"
if($LASTEXITCODE -ne 0){throw 'Installer tests failed.'}
