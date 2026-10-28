$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'test-results'
New-Item -ItemType Directory -Force -Path $out|Out-Null
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs=@('/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Net.Http.dll','/reference:System.Web.Extensions.dll')
foreach($suite in @(@{file='Tests.cs';main='III85III.Vpn.Tests';name='CoreTests'},@{file='LayoutTests.cs';main='III85III.Vpn.LayoutTests';name='LayoutTests'},@{file='LatencyTests.cs';main='III85III.Vpn.LatencyTests';name='LatencyTests'})){
 & $compiler /nologo /codepage:65001 /utf8output /target:exe /platform:x64 "/main:$($suite.main)" "/out:$out\$($suite.name).exe" @refs "$root\native\III85III.Vpn.cs" "$root\native\SignInForm.cs" "$root\native\VerificationForm.cs" "$root\native\$($suite.file)"
 if($LASTEXITCODE -ne 0){throw 'Native test compilation failed.'}
 & "$out\$($suite.name).exe" | Tee-Object -FilePath "$out\$($suite.name).txt"
 if($LASTEXITCODE -ne 0){throw 'Native test failed.'}
}
