param([string]$OutputDirectory,[string]$EngineDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root '.build\app' }
if (Test-Path -LiteralPath $out) { throw 'Choose an empty output directory to preserve existing builds.' }

New-Item -ItemType Directory -Path $out, "$out\core", "$out\licenses", "$out\source\native", "$out\source\tools", "$out\source\core" -Force | Out-Null

if($EngineDirectory){
    $engine=[IO.Path]::GetFullPath($EngineDirectory)
    if(-not(Test-Path -LiteralPath (Join-Path $engine 'iii85iii-core.exe')) -or -not(Test-Path -LiteralPath (Join-Path $engine 'iii85iii-tun.exe'))){throw 'The validated engine folder is incomplete.'}
    Get-ChildItem -LiteralPath $engine -File | Copy-Item -Destination "$out\core"
}else{
# 1. Compile III85III Go Core
Push-Location "$root\core"
try {
    Write-Output "Compiling iii85iii-core.exe..."
    $ldflags = "-s -w -X github.com/III85III/III85III-VPN/core/internal/version.Current=2.2.0"
    & go build -trimpath -ldflags $ldflags -o "$out\core\iii85iii-core.exe" ./cmd/firefox-vpn-core
    if ($LASTEXITCODE -ne 0) { throw "Go core build failed." }
    Copy-Item "$out\core\iii85iii-core.exe" "$out\core\firefox-vpn-core.exe" -Force

    Write-Output "Compiling iii85iii-tun.exe..."
    $ldflagsTun = "-s -w -H windowsgui -X github.com/III85III/III85III-VPN/core/internal/version.Current=2.2.0"
    & go build -tags with_gvisor -trimpath -ldflags $ldflagsTun -o "$out\core\iii85iii-tun.exe" ./cmd/firefox-vpn-tun
    if ($LASTEXITCODE -ne 0) { throw "Go tun build failed." }
    Copy-Item "$out\core\iii85iii-tun.exe" "$out\core\firefox-vpn-tun.exe" -Force

    # This build has no update key. Signed update operations are disabled;
    # an empty placeholder must not be presented as a real signature.
} finally {
    Pop-Location
}

# Copy wintun/wireguard dlls if present
$candidateDirs = @("$root\dist\III85III VPN\core", "$root\vendor\engine")
foreach ($dir in $candidateDirs) {
    if (Test-Path $dir) {
        Get-ChildItem -Path $dir -Filter "*.dll" -ErrorAction SilentlyContinue | ForEach-Object {
            Copy-Item $_.FullName "$out\core" -Force
        }
    }
}

}
# Generate manifest.json for engine files
$manifest = @{}
foreach ($file in (Get-ChildItem -Path "$out\core" -File | Where-Object {$_.Name -ne 'manifest.json'})) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest[$file.Name] = $hash
}
$manifestJson = $manifest | ConvertTo-Json -Depth 4
Set-Content -LiteralPath "$out\core\manifest.json" -Value $manifestJson -Encoding UTF8

# 2. Compile C# UI
Write-Output "Compiling C# UI..."
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs = @('/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Net.Http.dll','/reference:System.Web.Extensions.dll','/reference:Accessibility.dll')
& $compiler /nologo /codepage:65001 /utf8output /platform:x64 /target:winexe /optimize+ "/out:$out\III85III VPN.exe" "/win32icon:$root\native\assets\brand\iii85iii-luxury.ico" "/win32manifest:$root\native\app.manifest" @refs "$root\native\III85III.Vpn.cs" "$root\native\SignInForm.cs" "$root\native\VerificationForm.cs" "$root\native\Updates.cs"
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }

# 3. Copy Assets & Docs
if (Test-Path "$root\licenses") { Get-ChildItem -LiteralPath "$root\licenses" -File | Copy-Item -Destination "$out\licenses" }
if (Test-Path "$root\native\assets\fonts\OFL.txt") { Copy-Item -LiteralPath "$root\native\assets\fonts\OFL.txt" -Destination "$out\licenses\Lato-OFL.txt" }
if (Test-Path "$root\native\assets") { Copy-Item -LiteralPath "$root\native\assets" -Destination $out -Recurse -Force }
if (Test-Path "$root\docs") { Copy-Item -LiteralPath "$root\docs" -Destination $out -Recurse -Force }
if (Test-Path "$root\native") { Get-ChildItem -LiteralPath "$root\native" -File | Copy-Item -Destination "$out\source\native" }
if (Test-Path "$root\tools") { Get-ChildItem -LiteralPath "$root\tools" -File | Copy-Item -Destination "$out\source\tools" }

Write-Output "Built III85III VPN with newly customized III85III engine: $out"
