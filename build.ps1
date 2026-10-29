param([switch]$Test, [string]$OutputDirectory = 'dist')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$output = Join-Path $root $OutputDirectory
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$dependency = Join-Path $root '.deps\webview2'
if (-not (Test-Path "$dependency\lib\net462\Microsoft.Web.WebView2.Wpf.dll")) {
    New-Item -ItemType Directory -Force "$root\.deps" | Out-Null
    Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.4258.31/microsoft.web.webview2.1.0.4258.31.nupkg' -OutFile "$root\.deps\webview2.zip"
    Expand-Archive -LiteralPath "$root\.deps\webview2.zip" -DestinationPath $dependency -Force
}
New-Item -ItemType Directory -Force $output | Out-Null
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Web.Extensions.dll','System.Xaml.dll',"$framework\WPF\WindowsBase.dll","$framework\WPF\PresentationCore.dll","$framework\WPF\PresentationFramework.dll","$dependency\lib\net462\Microsoft.Web.WebView2.Core.dll","$dependency\lib\net462\Microsoft.Web.WebView2.Wpf.dll")
$options = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',"/out:$output\Mica.exe","/resource:$root\src\Shell.xaml,Mica.Shell.xaml","/resource:$root\src\ContextMenus.xaml,Mica.ContextMenus.xaml","/resource:$root\src\Library.xaml,Mica.Library.xaml")
$brandResources = Join-Path $output 'Mica.g.resources'
$brandWriter = New-Object System.Resources.ResourceWriter($brandResources)
$brandStream = New-Object System.IO.MemoryStream(,[IO.File]::ReadAllBytes("$root\src\Assets\Brand\Caveat.ttf"))
$brandWriter.AddResource('assets/brand/caveat.ttf', $brandStream)
$brandWriter.Generate()
$brandWriter.Close()
$brandStream.Dispose()
$options += "/resource:$brandResources,Mica.g.resources","/win32icon:$root\src\Assets\Brand\Mica.ico","/resource:$root\src\Assets\Brand\Mica.ico,Mica.Brand.ico"
foreach ($reference in $references) { $options += "/reference:$reference" }
$options += '/reference:System.Security.dll','/reference:System.Xml.dll','/reference:System.Xml.Linq.dll',"/reference:$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\System.Speech\v4.0_4.0.0.0__31bf3856ad364e35\System.Speech.dll"
$options += "$root\src\BrowserStoreBridge.cs","/resource:$root\src\StoreBridge.js,Mica.StoreBridge.js","$root\src\BrowserTools.cs","$root\src\BrowserDeveloperTools.cs","$root\src\BrowserMediaTools.cs","$root\tests\ToolsRegression.cs","$root\tests\StoreBridgeRegression.cs","/resource:$root\src\Ambient.js,Mica.Ambient.js","/resource:$root\src\Tetris.html,Mica.Tetris.html","/resource:$root\src\GameHub.html,Mica.GameHub.html","/resource:$root\src\Snake.html,Mica.Snake.html","/resource:$root\src\Game2048.html,Mica.Game2048.html","/resource:$root\src\Minesweeper.html,Mica.Minesweeper.html","/resource:$root\src\Breakout.html,Mica.Breakout.html","/resource:$root\src\Sudoku.html,Mica.Sudoku.html","/resource:$root\src\Memory.html,Mica.Memory.html","/resource:$root\src\Flappy.html,Mica.Flappy.html","/resource:$root\src\Pong.html,Mica.Pong.html","/resource:$root\src\Invaders.html,Mica.Invaders.html","/resource:$root\src\ConnectFour.html,Mica.ConnectFour.html","$root\src\BrowserGames.cs","$root\src\BrowserAiPanel.cs","$root\src\BrowserClaudeExtension.cs","$root\src\AiExtensions.cs","$root\src\BrowserImport.cs","$root\src\BrowserImportWindow.cs","$root\tests\AiPanelRegression.cs","$root\tests\ImportRegression.cs","$root\src\BrowserAutofill.cs","$root\src\BrowserLoginMenu.cs","$root\src\BrowserMemory.cs","$root\src\MicaMcp.cs","$root\src\BrowserIsland.cs","$root\src\DialogStyles.cs","$root\src\BrowserCaret.cs","$root\src\BrowserUpdates.cs","$root\src\AppUpdater.cs","$root\src\ReleaseKey.cs","$root\src\Totp.cs","$root\src\BrowserTotp.cs","$root\src\BrowserNavHistory.cs","$root\src\BrowserSignIn.cs","$root\src\BrowserPersonalize.cs","$root\src\BrowserFarewell.cs","$root\src\BrowserTicker.cs","$root\src\BrowserTabGroups.cs","$root\src\BrowserBlocker.cs","$root\src\BrowserExtensionToolbar.cs","$root\src\BrowserIslandDownloads.cs","$root\src\BrowserIslandZoom.cs","$root\src\BrowserBrand.cs","$root\src\BrowserCorners.cs","$root\src\BrowserSiteCard.cs","$root\src\BrowserReader.cs","$root\src\BrowserSplit.cs","$root\src\BrowserJars.cs","$root\src\BrowserTabDecor.cs","$root\src\BrowserCommands.cs","$root\src\BrowserReadLater.cs","$root\src\BrowserPeek.cs","$root\src\BrowserHighlights.cs","$root\src\BrowserPageTools.cs","$root\src\BrowserSendTab.cs","$root\src\BrowserSessions.cs","$root\src\BrowserProfiles.cs","$root\src\Localization.cs","$root\src\CrashLog.cs","$root\src\BrowserTextSize.cs","/resource:$root\src\Strings.vi.tsv,Mica.Strings.vi.tsv","/resource:$root\src\Reader.js,Mica.Reader.js","$root\tests\BrandRegression.cs","$root\tests\DevToolsSwitchRegression.cs","$root\tests\CornersRegression.cs","$root\tests\FeaturesRegression.cs","$root\tests\MoreFeaturesRegression.cs","$root\tests\PerfProbe.cs","$root\tests\IslandDownloadsRegression.cs","$root\tests\ExtensionToolbarRegression.cs","$root\tests\PersonalizeRegression.cs","$root\tests\TabsRegression.cs","$root\tests\MemoryBench.cs","$root\tests\BlockerRegression.cs","$root\tests\UpdatesRegression.cs","$root\tests\TotpRegression.cs","$root\tests\NavHistoryRegression.cs","$root\tests\ClaudeExtProbe.cs","$root\tests\ChatGptExtProbe.cs","$root\tests\KeysRegression.cs"
$options += "$root\src\WindowsIntegration.cs","$root\src\BrowserLaunch.cs","$root\src\BrowserHome.cs","$root\src\VaultModel.cs","$root\src\BrowserVault.cs","$root\tests\PrivacyRegression.cs"
$options += "$root\tests\VaultUiRegression.cs","$root\tests\HelloRegression.cs"
$options += '/reference:System.IO.Compression.dll',"$root\src\ExtensionPackage.cs","/resource:$root\src\Extensions.xaml,Mica.Extensions.xaml"
$options += "$root\tests\StoreRegression.cs","$root\src\BrowserDialogs.cs","$root\src\VaultLayout.cs","$root\src\WindowsHello.cs","$root\src\VaultUnlock.cs","$root\src\BundledRuntime.cs","$root\tests\RuntimeRegression.cs","$root\tests\DialogRegression.cs"
foreach ($icon in @('github','youtube','gmail','wikipedia')) { $options += "/resource:$root\src\Assets\Favicons\$icon.ico,Mica.Favicons.$icon.ico" }
& $compiler @options "$root\src\Core.cs" "$root\src\BrowserApp.cs" "$root\src\BrowserMotion.cs" "$root\src\BrowserWindow.cs" "$root\src\BrowserFullscreen.cs" "$root\src\BrowserAvatar.cs" "$root\src\BrowserPermissions.cs" "$root\src\BrowserPopups.cs" "$root\src\BrowserDownloadPrompt.cs" "$root\src\BrowserExtensionWindows.cs" "$root\src\BrowserLibrary.cs" "$root\src\BrowserDownloads.cs" "$root\src\DownloadModel.cs" "$root\tests\DownloadRegression.cs" "$root\tests\LibraryRegression.cs" "$root\src\BrowserContextMenus.cs" "$root\src\BrowserMenus.cs" "$root\src\BrowserIcons.cs" "$root\src\BrowserExtensions.cs" "$root\tests\ExtensionRegression.cs" "$root\tests\BrowserRegression.cs" "$root\tests\LayoutRegression.cs" "$root\tests\MotionRegression.cs" "$root\tests\SecurityToolsRegression.cs" "$root\src\BrowserSandbox.cs" "$root\src\BrowserDevToolsDock.cs" "$root\src\BrowserProtection.cs" "$root\src\EngineUpdater.cs" "$root\src\BrowserSettingsSync.cs" "$root\src\BrowserVaultSync.cs" "$root\tests\FullscreenRegression.cs" "$root\tests\ProtectionRegression.cs" "$root\tests\GamesRegression.cs" "$root\tests\AutofillRegression.cs" "$root\tests\MemoryRegression.cs" "$root\tests\GalleryRegression.cs"
if ($LASTEXITCODE -ne 0) { throw 'Mica compilation failed' }
Copy-Item "$dependency\lib\net462\Microsoft.Web.WebView2.Core.dll","$dependency\lib\net462\Microsoft.Web.WebView2.Wpf.dll" $output
Copy-Item "$dependency\runtimes\win-x64\native\WebView2Loader.dll" $output
Copy-Item "$root\src\Mica.exe.config" $output
# Built-in extensions (uBlock Origin Lite).
robocopy "$root\extensions" "$output\Extensions" /MIR /NFL /NDL /NJH /NJS /NP | Out-Null;if($LASTEXITCODE -ge 8){throw 'Copying built-in extensions failed'}
# robocopy reports 1-7 for success; don't let it become the script's exit code.
$global:LASTEXITCODE=0
Copy-Item "$root\src\Assets\Brand\OFL.txt" "$output\Caveat-OFL.txt"
Write-Host "Built $output\Mica.exe"
if ($Test) { & "$root\test.ps1" }

