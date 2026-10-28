param([string]$AppDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
& "$PSScriptRoot\package.ps1" -AppDirectory $AppDirectory
$version=(Get-Content -LiteralPath "$root\tools\project.json" -Raw|ConvertFrom-Json).version
$archive=Join-Path $root (".build\packages\III85III-VPN-v$version-windows-x64.zip")
$output=Join-Path $root 'III85III-VPN-Setup.exe'
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /codepage:65001 /utf8output /target:winexe /platform:x64 /optimize+ "/out:$output" "/resource:$archive,payload" "/win32icon:$root\native\assets\brand\iii85iii-luxury.ico" /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll "$root\native\Installer.cs" "$root\native\InstallPackage.cs"
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
Write-Output "Built single-file installer: $output"
