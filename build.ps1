$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    & $compiler /nologo /target:winexe /win32icon:LiveZ.ico /out:LiveZ.exe /resource:ui.html,ui /resource:LiveZ.ico,icon /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll LiveZ.cs AppAudio.cs CaptureOutline.cs
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o LiveZ.' }
    & $compiler /nologo /target:winexe /win32icon:LiveZ.ico /out:LiveZ-Instalar.exe /resource:LiveZ.exe,app /r:System.Windows.Forms.dll Setup.cs
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o instalador.' }
} finally {
    Pop-Location
}

