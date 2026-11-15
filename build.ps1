# Compila BigPicture.exe con il compilatore C# incluso in Windows (.NET Framework 4.8).
# Uso:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$fw  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
$out = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force $out | Out-Null

$src = Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$xaml = Join-Path $PSScriptRoot 'src\MainWindow.xaml'
$icon = Join-Path $PSScriptRoot 'src\app.ico'      # generata da tools\make_icon.py

& $csc /nologo /target:winexe /optimize+ /codepage:65001 /platform:anycpu `
    "/out:$out\BigPicture.exe" `
    "/lib:$fw\WPF" `
    /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll `
    /r:System.Xaml.dll /r:System.Web.Extensions.dll /r:System.Drawing.dll `
    "/resource:$xaml,MainWindow.xaml" `
    "/win32icon:$icon" `
    $src

if ($LASTEXITCODE -ne 0) { throw "Compilazione fallita ($LASTEXITCODE)" }
"OK -> $out\BigPicture.exe"
