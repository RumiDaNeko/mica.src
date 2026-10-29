$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$out=Join-Path $root 'artifacts/vault-tests'
New-Item -ItemType Directory -Force $out | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:exe /langversion:5 "/out:$out\VaultTests.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll "$root\src\Core.cs" "$root\src\VaultModel.cs" "$root\tests\VaultTests.cs"
if($LASTEXITCODE -ne 0){throw 'Vault test compile failed'}
& "$out\VaultTests.exe"
if($LASTEXITCODE -ne 0){throw 'Vault tests failed'}

