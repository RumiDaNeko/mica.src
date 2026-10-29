# Publishes the built installers as a GitHub release that Mica's updater picks up.
# Usage: .\publish-release.ps1 -Repo owner/name [-Notes 'What changed'] [-CrossRun id | -NoCross]
# Needs the GitHub CLI (gh) signed in. GitHub lists each asset's SHA-256 digest, and the release carries
# Mica's own signature for each installer; Mica checks both before it installs anything. Keep double quotes out of -Notes: Windows PowerShell
# splits them into separate gh arguments.
param([Parameter(Mandatory=$true)][string]$Repo,[string]$Notes='',[string]$Installer='',[string]$CrossRun='',[switch]$NoCross,[string]$SourceRepo='RumiDaNeko/mica.src')
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$version=([regex]::Match((Get-Content "$root\src\BrowserUpdates.cs" -Raw),'AppVersion="([^"]+)"')).Groups[1].Value
if(!$version){throw 'AppVersion not found in src\BrowserUpdates.cs'}
if(!$Installer){$Installer=Join-Path $root "installer-output\latest\Mica-$version-Setup.exe"}
if(!(Test-Path -LiteralPath $Installer)){throw "Build the installer first: $Installer"}
$updater=Get-Content "$root\src\AppUpdater.cs" -Raw
if($updater -notmatch "ReleaseRepo=`"$([regex]::Escape($Repo))`""){Write-Warning "src\AppUpdater.cs ReleaseRepo is not '$Repo'; installed copies won't look there."}
if(!$Notes){$Notes="Mica $version"}
$assets=@($Installer)
# The app-only update rides along, labelled with the engine it reuses; Mica takes it when its engine matches.
$update=Join-Path (Split-Path $Installer) "Mica-$version-Update.exe";$engine=Join-Path (Split-Path $Installer) "Mica-$version-Update.engine.txt"
if((Test-Path -LiteralPath $update) -and (Test-Path -LiteralPath $engine)){$assets+="$update#engine $((Get-Content -LiteralPath $engine -Raw).Trim())"}
else {Write-Warning 'No app-only update was built; installed copies will download the full installer.'}
$lite=Join-Path (Split-Path $Installer) "Mica-$version-Setup-Lite.exe";if(Test-Path -LiteralPath $lite){$assets+=$lite}
# The Mac disk images and the Linux AppImage come from the cloud: the Cross run for the commit being released
# (or -CrossRun). They ride along in the same release, signed like the rest, and the website offers them.
if(!$NoCross) {
 $commit=(git -C $root rev-parse HEAD).Trim()
 if(!$CrossRun){$CrossRun=(gh run list --repo $SourceRepo --workflow Cross --commit $commit --status success --limit 1 --json databaseId --jq '.[0].databaseId' 2>$null)}
 if(!$CrossRun){Write-Warning "No successful Cross run for $commit; this release carries Windows only. Pass -CrossRun <id> or -NoCross."}
 else {
  $crossDir=Join-Path (Split-Path $Installer) 'cross';if(Test-Path -LiteralPath $crossDir){Remove-Item -LiteralPath $crossDir -Recurse -Force}
  gh run download $CrossRun --repo $SourceRepo --dir $crossDir --pattern 'mica-*'
  if($LASTEXITCODE -ne 0){throw "Couldn't download the Mac and Linux files from Cross run $CrossRun"}
  $crossFiles=@(Get-ChildItem -LiteralPath $crossDir -Recurse -File | Where-Object {$_.Name -match '\.(dmg|AppImage)$'})
  $stale=@($crossFiles | Where-Object {$_.Name -notlike "Mica-$version-*"})
  if($stale){throw "Cross run $CrossRun built another version ($($stale[0].Name)); release $version needs its own build."}
  foreach($file in $crossFiles){$assets+=$file.FullName}
  Write-Host "Mac and Linux from Cross run ${CrossRun}: $(($crossFiles | ForEach-Object Name) -join ', ')"
 }
}
# Every installer is signed with Mica's release key ("<name>\n<sha-256>", RSA SHA-256): installed copies only run
# an update whose signature checks out against src\ReleaseKey.cs. The private key stays on this PC; back it up,
# because without it no further update can reach anyone.
$keyFile=if($env:MICA_RELEASE_KEY){$env:MICA_RELEASE_KEY}else{'D:\creds\mica-release-signing-key.xml'}
if(!(Test-Path -LiteralPath $keyFile)){throw "Release signing key not found: $keyFile"}
$signer=New-Object Security.Cryptography.RSACryptoServiceProvider;$signer.PersistKeyInCsp=$false;$signer.FromXmlString([IO.File]::ReadAllText($keyFile))
$published=([regex]::Match((Get-Content "$root\src\ReleaseKey.cs" -Raw),'Public="([^"]+)"')).Groups[1].Value
if($signer.ToXmlString($false) -ne $published){throw 'The signing key does not match src\ReleaseKey.cs; installed copies would refuse this release.'}
$lines=foreach($asset in $assets){$file=($asset -split '#')[0];$name=Split-Path $file -Leaf;$sha=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
 "$name "+[Convert]::ToBase64String($signer.SignData([Text.Encoding]::UTF8.GetBytes("$name`n$sha"),'SHA256'))}
$signatures=Join-Path (Split-Path $Installer) "Mica-$version-signatures.txt"
[IO.File]::WriteAllText($signatures,($lines -join "`n")+"`n",(New-Object Text.UTF8Encoding $false));$assets+=$signatures
# A draft first, then one file at a time: one slow upload can time out (HTTP 408), and retrying a single file
# is cheap. Installed copies only see the release once it is published, with every file in place.
$ErrorActionPreference='Continue'
gh release view "v$version" --repo $Repo --json isDraft 2>$null | Out-Null
if($LASTEXITCODE -ne 0){gh release create "v$version" --draft --repo $Repo --title "Mica $version" --notes $Notes;if($LASTEXITCODE -ne 0){throw 'gh release create failed'}}
foreach($asset in $assets){
 $sent=$false
 for($try=1;$try -le 4 -and !$sent;$try++){
  gh release upload "v$version" $asset --repo $Repo --clobber
  if($LASTEXITCODE -eq 0){$sent=$true}else{Write-Warning "Upload of $(Split-Path ($asset -split '#')[0] -Leaf) failed (try $try of 4)";Start-Sleep -Seconds (15*$try)}
 }
 if(!$sent){throw "Couldn't upload $asset. The release stays a draft; run this again to retry."}
}
gh release edit "v$version" --repo $Repo --draft=false --latest
if($LASTEXITCODE -ne 0){throw 'gh release edit failed; the release is still a draft'}
Write-Host "Published v$version to https://github.com/$Repo/releases/tag/v$version"
