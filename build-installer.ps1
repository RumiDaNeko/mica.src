param([string]$ReleaseDirectory='',[string]$OutputDirectory='')
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$version=([regex]::Match((Get-Content "$root\src\BrowserUpdates.cs" -Raw),'AppVersion="([^"]+)"')).Groups[1].Value
if(!$version){throw 'AppVersion not found in src\BrowserUpdates.cs'}
if(!$ReleaseDirectory){$ReleaseDirectory="release-v$version"}
$release=Join-Path $root $ReleaseDirectory
$out=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $root 'installer-output'}
New-Item -ItemType Directory -Force $out | Out-Null
if(!(Test-Path "$release\Runtime\msedgewebview2.exe")){throw 'Bundle the release Runtime before building the installer.'}
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$brandWriter=New-Object System.Resources.ResourceWriter("$out\MicaSetup.g.resources")
$brandStream=New-Object System.IO.MemoryStream(,[IO.File]::ReadAllBytes("$root\src\Assets\Brand\Caveat.ttf"))
$brandWriter.AddResource('assets/brand/caveat.ttf',$brandStream)
$brandWriter.Generate();$brandWriter.Close();$brandStream.Dispose()
$references=@('System.dll','System.Core.dll','System.Xaml.dll','System.IO.Compression.dll',"$framework\WPF\WindowsBase.dll","$framework\WPF\PresentationCore.dll","$framework\WPF\PresentationFramework.dll")
$options=@('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',"/out:$out\MicaSetup.exe","/win32icon:$root\src\Assets\Brand\Mica.ico","/resource:$root\installer\Setup.xaml,Mica.Setup.xaml","/resource:$out\MicaSetup.g.resources,MicaSetup.g.resources")
foreach($reference in $references){$options+="/reference:$reference"}
& "$framework\csc.exe" @options "$root\installer\Setup.cs" "$root\installer\CloseBrowser.cs" "$root\installer\InstallEngine.cs" "$root\installer\Lzms.cs" "$root\src\WindowsIntegration.cs"
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
Copy-Item "$out\MicaSetup.exe" "$release\MicaUninstall.exe"
Copy-Item "$root\src\Mica.exe.config" "$release\MicaUninstall.exe.config"
# Engine parts the WebView2 runtime ships for Edge but Mica never loads: Copilot, IE mode, Application Guard,
# the 32-bit loader, and languages beyond the most used ones (a missing language falls back to English).
$skip=@('Runtime/copilotapp.exe','Runtime/mscopilot.exe','Runtime/Copilot.dat','Runtime/unified_copilot_telemetry.dll','Runtime/dual_engine_adapter_x64.dll','Runtime/wdag.dll','Runtime/EBWebView/x86/EmbeddedBrowserWebView.dll')
$keepLocales=@('en-US','en-GB','vi','zh-CN','zh-TW','ja','ko','es','es-419','fr','de','it','pt-BR','pt-PT','ru','tr','pl','nl','id','th','ar','hi','uk','cs','sv')
# Build the payload in the system temp directory; D: may have limited free space.
Add-Type -AssemblyName System.IO.Compression
Add-Type -Path "$root\installer\Lzms.cs"
$engineVersion=([regex]::Match((Get-Content "$release\bundled-engine.json" -Raw),'"version"\s*:\s*"([0-9.]+)"')).Groups[1].Value
if(!$engineVersion){throw 'bundled-engine.json has no version'}
# Three installers from one release folder: the full one; an app-only update that leaves the engine out
# and reuses the one already installed (it names the engine it expects); and a lite one for new installs
# that uses Windows' own WebView2 runtime.
function Build-Setup([string]$name,[string]$kind) {
 $payload=Join-Path ([IO.Path]::GetTempPath()) ('mica-payload-'+[guid]::NewGuid().ToString('N')+'.zip')
 try {
  $stream=[IO.File]::Create($payload)
  $zip=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create,$false)
  $raw=0L;$left=0
  try {
   foreach($file in Get-ChildItem -LiteralPath $release -File -Recurse) {
    $relative=$file.FullName.Substring($release.Length+1).Replace('\','/')
    if($relative.EndsWith('.g.resources')){continue}
    if($kind -ne 'full' -and $relative.StartsWith('Runtime/')){$left++;continue}
    if($kind -eq 'lite' -and $relative -eq 'bundled-engine.json'){$left++;continue}
    if($skip -contains $relative -or $relative -like 'Runtime/copilot_overlay_*'){$left++;continue}
    if($relative -like 'Runtime/Locales/*.pak' -and $keepLocales -notcontains [IO.Path]::GetFileNameWithoutExtension($relative)){$left++;continue}
    # Each file is LZMS-packed and stored as is; ZIP only keeps the names.
    $entry=$zip.CreateEntry($relative,[IO.Compression.CompressionLevel]::NoCompression)
    $outputFile=$entry.Open()
    try {[Mica.Lzms]::Pack($file.FullName,$outputFile)}finally {$outputFile.Dispose()}
    $raw+=$file.Length
   }
   $meta=@{'payload-size'="$raw"}
   if($kind -eq 'app'){$meta['payload-kind']='app';$meta['engine-version']=$engineVersion}
   if($kind -eq 'lite') {
    # Tells Mica to use Windows' WebView2 runtime; packed like every other file.
    $meta['payload-kind']='lite';$mode=[IO.Path]::GetTempFileName();[IO.File]::WriteAllText($mode,'evergreen')
    try {$entry=$zip.CreateEntry('engine-mode.txt',[IO.Compression.CompressionLevel]::NoCompression);$outputFile=$entry.Open();try {[Mica.Lzms]::Pack($mode,$outputFile)}finally {$outputFile.Dispose()}}finally {Remove-Item -LiteralPath $mode}
   }
   foreach($key in $meta.Keys) {
    $metaEntry=$zip.CreateEntry($key,[IO.Compression.CompressionLevel]::NoCompression)
    $writer=New-Object IO.StreamWriter($metaEntry.Open());try {$writer.Write($meta[$key])}finally {$writer.Dispose()}
   }
  }finally {$zip.Dispose();$stream.Dispose()}
  Write-Host ("{0}: packed {1:N0} MB of files, left out {2}" -f $name,($raw/1MB),$left)
  $setup=Join-Path $out $name
  # Write stub + payload in one pass: reopening a just-copied EXE races antivirus scans.
  $stub=[IO.File]::ReadAllBytes("$out\MicaSetup.exe")
  $inputFile=[IO.File]::OpenRead($payload);$sha=[Security.Cryptography.SHA256]::Create()
  try {$hash=$sha.ComputeHash($inputFile);$length=$inputFile.Length;$inputFile.Position=0
   $outputFile=[IO.File]::Create($setup)
   try {$outputFile.Write($stub,0,$stub.Length);$inputFile.CopyTo($outputFile);$writer=New-Object IO.BinaryWriter($outputFile);$writer.Write([long]$length);$writer.Write([byte[]]$hash);$writer.Write([Text.Encoding]::ASCII.GetBytes('MICAZIP2'));$writer.Flush()}finally {$outputFile.Dispose()}
  }finally {$sha.Dispose();$inputFile.Dispose()}
  Write-Host "Installer ready: $setup"
  Get-FileHash -LiteralPath $setup -Algorithm SHA256 | Format-List
 }finally {if(Test-Path -LiteralPath $payload){Remove-Item -LiteralPath $payload}}
}
Build-Setup "Mica-$version-Setup.exe" 'full'
Build-Setup "Mica-$version-Update.exe" 'app'
Build-Setup "Mica-$version-Setup-Lite.exe" 'lite'
Set-Content -LiteralPath (Join-Path $out "Mica-$version-Update.engine.txt") -Value $engineVersion -Encoding ascii
