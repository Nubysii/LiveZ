# LiveZ

Aplicativo para compartilhar tela e áudio com amigos no Windows 10 e 11 de 64 bits. Os convidados entram pelo navegador, pedem aprovação e podem apresentar quando a sala está livre.

## Usar

O estúdio permite escolher a tela, ajustar o volume e gerar um convite temporário.

O filtro de áudio do anfitrião reconhece o Discord de desktop. Chamadas no Discord Web e o áudio compartilhado pelo navegador de um convidado podem entrar na transmissão. Consulte o [guia de uso](./Como%20usar.txt) para detalhes de conectividade, áudio e TURN.

## Compilar no Windows

Execute o script na pasta do projeto com o Windows PowerShell:

~~~powershell
.\build.ps1
~~~

O script usa o compilador C# do .NET Framework, incorpora o painel e o ícone e gera o aplicativo portátil e o instalador.

## Licença

O código-fonte do LiveZ usa a [licença MIT](./LICENSE).

## Aviso sobre o instalador

O Microsoft Defender sinalizou o instalador anterior como Trojan:Win32/Wacatac.C!ml. Mantenha a cópia sinalizada em quarentena enquanto a detecção estiver em análise.
