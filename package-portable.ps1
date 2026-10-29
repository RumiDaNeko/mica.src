param([string]$OutputDirectory='release-v0.9')
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$version='156.0.4314.42'
$url='https://msedge.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/074c0930-36ad-4756-9d04-dfe0e503bd94/Microsoft.WebView2.FixedVersionRuntime.156.0.4314.42.x64.cab'
$expectedHash='CB4612CE79A62A0BE094301A72F73CF91BB250F5E83DC51886F66A083FC0694F'
$cab=Join-Path $root '.deps/webview2-fixed-x64.cab'
$extract=Join-Path $root '.deps/fixed-extracted'
$source=Join-Path $extract "Microsoft.WebView2.FixedVersionRuntime.$version.x64"
if(!(Test-Path $cab)){Invoke-WebRequest $url -OutFile $cab}
if((Get-FileHash $cab -Algorithm SHA256).Hash -ne $expectedHash){throw 'Runtime download hash does not match the pinned Microsoft package.'}
New-Item -ItemType Directory -Force $extract | Out-Null
# Always extract the verified CAB so a partial previous extraction cannot be reused.
& expand.exe $cab '-F:*' $extract | Out-Null
if($LASTEXITCODE -ne 0){throw 'Runtime extraction failed'}
$signature=Get-AuthenticodeSignature (Join-Path $source 'msedgewebview2.exe')
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){throw 'Runtime executable does not have a valid Microsoft signature'}
& "$root/build.ps1" -OutputDirectory $OutputDirectory
$output=Join-Path $root $OutputDirectory
$runtime=Join-Path $output 'Runtime'
New-Item -ItemType Directory -Force $runtime | Out-Null
Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $runtime -Recurse -Force
@{version=$version;architecture='x64';source=$url;cabSha256=$expectedHash;distribution='Microsoft WebView2 Fixed Version Runtime'} | ConvertTo-Json | Set-Content (Join-Path $output 'bundled-engine.json') -Encoding UTF8
Copy-Item "$root/docs/portable-runtime.md" "$output/READ-ME.md"
Write-Host "Portable build ready: $output"
