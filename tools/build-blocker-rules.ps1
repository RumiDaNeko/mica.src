# Builds extensions\mica-blocker\rules\ from uBlock Origin Lite's rulesets: one file per list, an index
# (lists.json) and the default choice (default.json).
# The engine doesn't apply an extension's static rulesets, but it does apply dynamic rules, so Mica's
# blocker extension loads the chosen lists as dynamic rules (Chromium allows 30,000, and 1,000 regex).
# Only rules that stand on their own are kept: block, allow, allowAllRequests and upgradeScheme.
# Redirects point at uBO Lite's own files, and header rules count against a much smaller limit.
param([string]$Root=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$ubol=Join-Path $Root 'extensions\ublock-lite'
$out=Join-Path $Root 'extensions\mica-blocker\rules'
$utf8=New-Object Text.UTF8Encoding($false)
$keep='block','allow','allowAllRequests','upgradeScheme'
if(Test-Path $out){Remove-Item $out -Recurse -Force};New-Item -ItemType Directory $out | Out-Null
$lists=New-Object System.Collections.Generic.List[object];$defaults=@()
foreach($detail in (Get-Content (Join-Path $ubol 'rulesets\ruleset-details.json') -Raw -Encoding UTF8 | ConvertFrom-Json)) {
 $file=Join-Path $ubol "rulesets\main\$($detail.id).json";if(-not(Test-Path $file)){continue}
 if($detail.id -eq 'ubol-tests' -or $detail.id -eq 'ublock-experimental'){continue}
 $all=Get-Content $file -Raw | ConvertFrom-Json;$rules=@($all | Where-Object {$keep -contains $_.action.type -and -not $_.condition.regexFilter})
 if($rules.Count -eq 0){continue}
 $id=1;foreach($rule in $rules){$rule.id=$id;$id++}
 [IO.File]::WriteAllText((Join-Path $out "$($detail.id).json"),(ConvertTo-Json @($rules) -Depth 30 -Compress),$utf8)
 $group=if($detail.group){$detail.group}elseif($detail.lang){'regions'}else{'misc'}
 $lists.Add([ordered]@{id=$detail.id;name=$detail.name;group=$group;lang=$detail.lang;rules=$rules.Count;default=[bool]$detail.enabled})
 if($detail.enabled){$defaults+=$detail.id}
}
[IO.File]::WriteAllText((Join-Path $out 'lists.json'),(ConvertTo-Json @($lists.ToArray()) -Depth 5 -Compress),$utf8)
[IO.File]::WriteAllText((Join-Path $out 'default.json'),(ConvertTo-Json @{lists=$defaults;excluded=@()} -Compress),$utf8)
# The extension's version follows uBO Lite's, so a new bundled copy reloads its rules.
$version=(Get-Content (Join-Path $ubol 'manifest.json') -Raw | ConvertFrom-Json).version
$manifest=Join-Path $Root 'extensions\mica-blocker\manifest.json';$m=Get-Content $manifest -Raw | ConvertFrom-Json;$m.version="$version.2"
[IO.File]::WriteAllText($manifest,(ConvertTo-Json $m -Depth 5),$utf8)
"{0} lists, {1} rules by default ({2})" -f $lists.Count,(($lists | Where-Object {$_.default} | ForEach-Object {$_.rules}) | Measure-Object -Sum).Sum,($defaults -join ', ')
