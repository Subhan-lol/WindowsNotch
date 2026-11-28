# Builds Notch Island with the C# compiler that ships inside Windows (.NET Framework 4.8).
# No SDK, no NuGet, no downloads. Output: bin\NotchIsland.exe
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw   = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpf  = "$fw\WPF"
$wm   = "$env:WINDIR\System32\WinMetadata"
$out  = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $out | Out-Null
$ico = Join-Path $root 'assets\notch.ico'
if (-not (Test-Path $ico)) { & (Join-Path $root 'make-icon.ps1') -Out $ico }

$refs = @(
  "$wpf\PresentationCore.dll", "$wpf\PresentationFramework.dll", "$wpf\WindowsBase.dll",
  "$fw\System.Xaml.dll", "$fw\System.dll", "$fw\System.Core.dll", "$fw\System.Xml.dll",
  "$fw\System.Drawing.dll", "$fw\System.Windows.Forms.dll", "$wpf\System.Speech.dll", "$fw\System.Web.Extensions.dll", "$fw\System.Data.dll",
  "$fw\System.Runtime.dll", "$fw\System.Runtime.InteropServices.WindowsRuntime.dll",
  "$wm\Windows.Foundation.winmd", "$wm\Windows.Media.winmd", "$wm\Windows.Storage.winmd"
) | ForEach-Object { "/r:$_" }

$res = @(
  "/resource:$root\ui\Notch.xaml,Notch.xaml",
  "/resource:$root\ui\Styles.xaml,Styles.xaml",
  "/resource:$ico,notch.ico",
  "/resource:$root\ui\phone.html,phone.html"
)
$src = Get-ChildItem "$root\src\*.cs" | ForEach-Object { $_.FullName }

& "$fw\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu /warn:3 /nowarn:0169,0414,0649 `
  "/out:$out\NotchIsland.exe" "/win32icon:$ico" "/win32manifest:$root\app.manifest" `
  @refs @res @src
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Copy-Item "$root\App.config" "$out\NotchIsland.exe.config" -Force
Write-Host "Built $out\NotchIsland.exe" -ForegroundColor Green
