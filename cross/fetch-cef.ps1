# Downloads the CEF build named in cef.lock for a Windows runtime identifier, checks its SHA-1 and unpacks
# libcef.dll, the other Release files, the resources and locales\ side by side into cef\<rid>\ (the layout the
# Windows Chromium package had). Mica.Cross.csproj runs it before a build when cef\<rid>\.version doesn't match
# cef.lock. Windows only builds the shell for development; Mica on Windows ships the WebView2 build.
#   powershell -File fetch-cef.ps1 -Rid win-x64        (win-x64, win-arm64)
param([Parameter(Mandatory=$true)][string]$Rid)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-Location $PSScriptRoot
$platform = @{ 'win-x64' = 'windows64'; 'win-arm64' = 'windowsarm64' }[$Rid]
if (-not $platform) { throw "fetch-cef.ps1: unsupported runtime identifier $Rid" }
$lock = @{}
foreach ($line in Get-Content -Encoding UTF8 cef.lock) { if ($line -match '^(\w+)\s+(\S+)$') { $lock[$Matches[1]] = $Matches[2] } }
$version = $lock['version']; $sha1 = $lock[$platform]
if (-not $version -or -not $sha1) { throw "fetch-cef.ps1: cef.lock has no entry for $platform" }

$dest = Join-Path $PSScriptRoot "cef\$Rid"
$stamp = Join-Path $dest '.version'
if ((Test-Path $stamp) -and ((Get-Content -Raw $stamp) -eq $version)) { exit 0 }
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'cef') | Out-Null
Set-Content -NoNewline -Encoding ASCII (Join-Path $PSScriptRoot 'cef\.gitignore') "*`n"

$name = "cef_binary_${version}_${platform}_minimal.tar.bz2"
$archive = Join-Path $PSScriptRoot "cef\$name"
$staging = Join-Path $PSScriptRoot "cef\.unpack-$Rid"
if (-not (Test-Path $archive)) {
  Write-Host "Downloading CEF $version for $platform..."
  [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
  $client = New-Object Net.WebClient
  $client.DownloadFile("https://cef-builds.spotifycdn.com/" + $name.Replace('+', '%2B'), "$archive.part")
  Move-Item -Force "$archive.part" $archive
}
$actual = (Get-FileHash -Algorithm SHA1 $archive).Hash.ToLowerInvariant()
if ($actual -ne $sha1) { Remove-Item -Force $archive; throw "fetch-cef.ps1: $name has SHA-1 $actual, cef.lock expects $sha1" }

foreach ($dir in $staging, $dest) { if (Test-Path $dir) { Remove-Item -Recurse -Force $dir } }
New-Item -ItemType Directory -Force $staging, $dest | Out-Null
# Windows' own tar (bsdtar, Windows 10 1803+) reads .tar.bz2; Git's GNU tar would take "C:" for a remote host.
& "$env:SystemRoot\System32\tar.exe" -xjf $archive -C $staging
if ($LASTEXITCODE -ne 0) { throw "fetch-cef.ps1: couldn't unpack $name (tar $LASTEXITCODE)" }
$root = Get-ChildItem -Directory $staging | Select-Object -First 1
# The import library and the sandbox bootstrap executables aren't used: CefGlue loads libcef.dll itself.
Get-ChildItem (Join-Path $root.FullName 'Release') | Where-Object { $_.Name -notlike '*.lib' -and $_.Name -notlike 'bootstrap*.exe' } | Copy-Item -Destination $dest -Recurse
Get-ChildItem (Join-Path $root.FullName 'Resources') | Copy-Item -Destination $dest -Recurse
Remove-Item -Recurse -Force $staging
Remove-Item -Force $archive
Set-Content -NoNewline -Encoding ASCII $stamp $version
Write-Host "CEF $version is in $dest"
