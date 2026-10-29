$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force "$PSScriptRoot\artifacts" | Out-Null
& $compiler /nologo /target:exe /out:"$PSScriptRoot\artifacts\CoreTests.exe" /reference:System.Web.Extensions.dll "$PSScriptRoot\src\Core.cs" "$PSScriptRoot\tests\CoreTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& "$PSScriptRoot\artifacts\CoreTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Behavior tests failed' }
