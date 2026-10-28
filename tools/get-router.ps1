param([string]$Version='v1.14.4')
$ErrorActionPreference='Stop'
if($Version -notmatch '^v\d+\.\d+\.\d+$'){throw 'Choose an explicit release version.'}
$root=Split-Path -Parent $PSScriptRoot
$vendor=Join-Path $root 'vendor'
New-Item -ItemType Directory -Path $vendor -Force | Out-Null
$release=Invoke-RestMethod -Uri ("https://api.github.com/repos/SagerNet/sing-box/releases/tags/"+$Version) -Headers @{'User-Agent'='III85III-VPN-development'}
$asset=$release.assets | Where-Object {$_.name -match '^sing-box-[0-9.]+-windows-amd64\.zip$'} | Select-Object -First 1
if(-not $asset){throw 'Official Windows amd64 release asset was not found.'}
if($asset.digest -notmatch '^sha256:[a-fA-F0-9]{64}$'){throw 'No official SHA256 digest; verify release manually before use.'}
$zip=Join-Path $vendor $asset.name
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip
$actual=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if($actual -ne $asset.digest.Substring(7).ToLowerInvariant()){throw 'Router download checksum mismatch.'}
$dest=Join-Path $vendor ('sing-box-'+$release.tag_name)
if(-not(Test-Path -LiteralPath $dest)){Expand-Archive -LiteralPath $zip -DestinationPath $dest}
@{dependency='sing-box';release=$release.tag_name;source='https://github.com/SagerNet/sing-box';asset=$asset.name;sha256=$actual;license='GPL-3.0-or-later';verified=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $vendor 'router-provenance.json') -Encoding UTF8
Write-Output ('Verified router '+$release.tag_name+' at '+$dest)
