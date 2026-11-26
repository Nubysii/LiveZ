param([Parameter(Mandatory=$true)][string]$FfmpegPackage)
$ErrorActionPreference='Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try{
    $out='dist-nativo'
    New-Item -ItemType Directory -Force $out | Out-Null
    if((Get-FileHash $FfmpegPackage).Hash -ne 'B9BBBB3ACECE000CBEEC994F856649354E52C773807841E78908374B24D658FD'){throw 'Use o pacote ffmpeg-9.1-essentials_build.zip da release 9.1 de GyanD/codexffmpeg.'}
    $deps=Join-Path $env:TEMP 'livez-ffmpeg-native'
    New-Item -ItemType Directory -Force $deps | Out-Null
    tar -xf $FfmpegPackage -C $deps
    if($LASTEXITCODE -ne 0){throw 'Falha ao extrair FFmpeg.'}
    $d=Join-Path $deps 'ffmpeg-9.1-essentials_build'
    rustc --edition=2021 -C opt-level=3 nativo/room.rs -o "$out/LiveZ.Room.exe"
    if($LASTEXITCODE -ne 0){throw 'Falha ao compilar a distribuição de mídia.'}
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    & $compiler /nologo /platform:x64 /target:winexe /out:dist-nativo\LiveZ.exe /win32icon:LiveZ.ico /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll nativo\Studio.cs nativo\Media.cs nativo\Tunnel.cs AppAudio.cs CaptureOutline.cs
    if($LASTEXITCODE -ne 0){throw 'Falha ao compilar o estúdio.'}
    Copy-Item "$d/bin/ffmpeg.exe","$d/bin/ffplay.exe",LICENSE,nativo/LEIA-ME.txt $out -Force
    Copy-Item "$d/LICENSE" "$out/FFmpeg-LICENSE.txt" -Force
    Copy-Item "$d/README.txt" "$out/FFmpeg-README.txt" -Force
    $files='LiveZ.exe','LiveZ.Room.exe','ffmpeg.exe','ffplay.exe','LICENSE','LEIA-ME.txt','FFmpeg-LICENSE.txt','FFmpeg-README.txt'
    Compress-Archive -LiteralPath @($files|ForEach-Object{Join-Path $out $_}) -DestinationPath LiveZ-Nativo-Windows-x64.zip -Force
}finally{Pop-Location}
