# Windows counterpart of test.sh, for development (Mica on Windows ships the WebView2 build): builds the shell, runs
# the engine chooser/updater self-test, then the browser self-test in an off-screen window. -Claude also installs
# Claude for Chrome into the throwaway test profile. Results: cross\out\engine-self-test.txt, cross\out\self-test.txt.
#   powershell -File cross\test.ps1 [-Claude]
param([switch]$Claude)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not $env:DOTNET_ROOT -and (Test-Path "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe")) {
  $env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
}
$platform = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }
$rid = if ($platform -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
dotnet build -c Debug -p:Platform=$platform
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$app = "bin\$platform\Debug\net8.0\$rid\Mica.exe"
New-Item -ItemType Directory -Force out | Out-Null
$result = Join-Path $PWD 'out\self-test.txt'; $testProfile = Join-Path $PWD 'out\test-profile'; $engine = Join-Path $PWD 'out\engine-self-test.txt'
foreach ($path in $result, $engine) { if (Test-Path $path) { Remove-Item -Force $path } }
if (Test-Path $testProfile) { Remove-Item -Recurse -Force $testProfile }

& $app --engine-self-test $engine | Out-Null
$engineCode = $LASTEXITCODE
Get-Content $engine | Where-Object { $_ -notlike 'PASS*' }
if ($engineCode -ne 0) { Get-Content $engine; exit $engineCode }

$arguments = @('--self-test', $result, '--profile', $testProfile)
if ($Claude) { $arguments += @('--install-extension', 'fcoeoabgfenejglbffodgkkbkcdhcgfn') }
$process = Start-Process -FilePath $app -ArgumentList $arguments -PassThru -Wait -NoNewWindow
if (Test-Path $result) { Get-Content $result } else { "No result file: the app exited early (code $($process.ExitCode))." }
"Engine log: $testProfile\engine.log"
exit $process.ExitCode
