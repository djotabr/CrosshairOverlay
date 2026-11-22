# Crosshair Overlay

Mira sobreposta para jogos, por **DJotaBR**.

**Baixar:** [última versão](https://github.com/djotabr/CrosshairOverlay/releases/latest/download/CrosshairOverlay.exe). Depois de instalado, o app avisa quando sai versão nova.

## Novidades da 2.0.0

**Menu novo**
- O menu agora é um **painel lateral** que entra deslizando pelo atalho (Ctrl+Alt+C) e fecha com Esc.
- Dá para escolher a **posição**: esquerda (padrão), direita, em cima, embaixo ou janela no meio da tela, como antes.
- O **visual é moderno**: painel flutuante com cantos arredondados, seções em cartões, abas com ícones, chaves liga/desliga, sliders finos e prévia da mira sempre visível.
- Tem uma aba nova, **Perfis**, e uma aba **Aparência** para posição, tema e transparência.

**Temas de cores**
- **Windows 11**, o tema padrão, acompanha o modo claro/escuro e a cor de destaque do Windows, inclusive quando você muda.
- O **Windows XP** tem faixa azul no topo, fundo bege, botão fechar vermelho e as fontes Tahoma e Trebuchet.
- Também há Verde, Azul gelo, Roxo neon, Vermelho tático, Laranja, Militar, Rosa, Ciano, Preto e dourado, e Claro.
- No **tema próprio**, você muda cada cor (destaque, fundo, abas, campos, bordas e textos).

**Grupo**
- As **senhas** podem ser compartilhadas com o grupo, criptografadas, ou ficar só no seu PC. Na caixinha de anotar, Tab troca entre as duas.
- **Prints:** Ctrl+Alt+S tira o print e manda para o grupo, e Ctrl+Alt+O abre os prints recebidos por cima do jogo.

**Atualização**
- Quando sai versão nova, o app **pergunta antes de baixar** e, depois de baixar, **pergunta se pode reiniciar** para instalar.
- Durante o jogo ele não interrompe: espera você sair do jogo para perguntar.
- As versões chegam pelo GitHub ou pela sala do grupo. O app só aceita versões assinadas pelo autor.

**Outros**
- A mira, as senhas e o contador só aparecem **com o jogo em foco**, e somem ao minimizar ou sair.
- O atalho do print mudou de Ctrl+Alt+P para **Ctrl+Alt+S**, porque o P já era o "próximo perfil".

## O que tem
- Mira personalizável com perfis, e troca de perfil automática por jogo, com o ícone do jogo detectado
- Só aparece com o jogo em foco
- Auto-tecla: até 3 teclas, intervalo e duração fixos ou aleatórios, com contador na tela e bipe
- Senhas na tela: suas ou compartilhadas com o grupo (criptografadas, via ntfy.sh)
- Print da tela enviado para o grupo
- Menu lateral com temas e 5 posições
- Atualização pelo GitHub e pelo grupo, com confirmação e só versões assinadas pelo autor

## Atalhos padrão
| Ação | Atalho |
|---|---|
| Abrir/fechar o menu | Ctrl+Alt+C |
| Mostrar/ocultar a mira | Ctrl+Alt+H |
| Próximo perfil | Ctrl+Alt+P |
| Ligar/desligar auto-tecla | Ctrl+Alt+K |
| Anotar senha | Ctrl+Alt+N (Tab troca entre grupo e só minha) |
| Apagar senha | Ctrl+Alt+D e o número |
| Mostrar/ocultar senhas | Ctrl+Alt+M |
| Tirar print e enviar | Ctrl+Alt+S |
| Ver prints | Ctrl+Alt+O |

Todos os atalhos podem ser trocados no menu.

## Compilar
`build.ps1` compila com o C# que já vem no Windows (.NET Framework 4). `publish.ps1 "o que mudou"` gera, assina e publica uma versão no GitHub.
