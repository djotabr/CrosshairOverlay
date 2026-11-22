# Crosshair Overlay

Mira sobreposta para jogos, por **DJotaBR**.

**Baixar:** [última versão](https://github.com/djotabr/CrosshairOverlay/releases/latest/download/CrosshairOverlay.exe). Depois de instalado, o app se atualiza sozinho.

## O que tem
- Mira personalizável com perfis, e troca de perfil automática por jogo
- Só aparece com o jogo em foco (some ao minimizar ou sair)
- Auto-tecla: até 3 teclas, intervalo e duração fixos ou aleatórios, com contador na tela
- Senhas na tela: suas ou compartilhadas com o grupo (criptografadas, via ntfy.sh)
- Print da tela enviado para o grupo
- Atualização automática pelo GitHub e pelo grupo, só aceita versões assinadas pelo autor

## Atalhos padrão
| Ação | Atalho |
|---|---|
| Anotar senha | Ctrl+Alt+N (Tab troca grupo / só minha) |
| Apagar senha | Ctrl+Alt+D e o número |
| Mostrar/ocultar senhas | Ctrl+Alt+M |
| Tirar print e enviar | Ctrl+Alt+P |
| Ver prints | Ctrl+Alt+O |
| Auto-tecla | Ctrl+Alt+K |

## Compilar
`build.ps1` compila com o C# que já vem no Windows (.NET Framework 4). `publish.ps1` gera, assina e publica uma versão no GitHub.
