# LiveZ Nativo

Compartilhe a tela ou uma janela com áudio em um aplicativo Windows de 64 bits. A edição nativa usa Windows Forms, um serviço de sala em Rust e FFmpeg para captura, codificação e reprodução.

## Baixar e usar

Baixe o ZIP na [página de versões](https://github.com/Nubysii/LiveZ/releases/latest), extraia todos os arquivos e abra LiveZ.exe. Quem transmite e quem assiste usa a mesma edição.

1. O anfitrião clica em **Gerar convite** e compartilha o link.
2. O convidado cola o link em **Entrar na sala de um amigo**.
3. O anfitrião aprova a entrada e escolhe uma janela ou um monitor para transmitir.

O áudio e o vídeo passam pelo túnel da sala. Veja o [guia de uso](./nativo/LEIA-ME.txt) para detalhes sobre qualidade, áudio e convidados. A edição é destinada ao Windows 10 e 11 x64. A captura e a reprodução foram verificadas no Windows 10.

## Compilar

Use o Rust estável x64 com um linker para Windows e o compilador C# do .NET Framework. Baixe o arquivo ffmpeg-9.1-essentials_build.zip na [versão 9.1 de GyanD/codexffmpeg](https://github.com/GyanD/codexffmpeg/releases/tag/9.1). Execute o comando a partir da raiz do projeto.

~~~powershell
.\nativo\build.ps1 -FfmpegPackage 'C:\Downloads\ffmpeg-9.1-essentials_build.zip'
~~~

O script verifica o SHA-256 da dependência e gera LiveZ-Nativo-Windows-x64.zip.

## Licenças

O código do LiveZ usa a [licença MIT](./LICENSE). O pacote inclui o FFmpeg e o FFplay sob GPLv3, com os avisos e os detalhes da compilação em FFmpeg-LICENSE.txt e FFmpeg-README.txt. [Código-fonte correspondente do FFmpeg](https://github.com/FFmpeg/FFmpeg/commit/74e4393595)

O Microsoft Defender sinalizou o instalador de uma edição anterior como Trojan:Win32/Wacatac.C!ml. Mantenha a cópia sinalizada em quarentena enquanto a detecção estiver em análise.
