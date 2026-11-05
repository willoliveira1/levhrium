# Levhrium

Navegador Chromium leve para uso diário pessoal, com containers por aba.

## Objetivo

Dois requisitos mandam no projeto: **RAM baixa** e **múltiplas contas do mesmo site
simultaneamente** (caso motivador: duas contas do PokeIdle, que permite mais de uma
conta mas só uma por navegador).

- **Plataforma:** Windows 10 22H2 x64 e Windows 11 x64. Nenhuma concessão a portabilidade.
- **Métrica primária:** RAM. Secundária: abertura a frio. Disco: irrelevante.
- **Medido (M3):** as mesmas 10 abas, no mesmo Chromium 156, custam **538 MB no
  Levhrium contra 1270 MB no Chrome** — 57,7% menos. Métrica: working set privado
  somado em toda a árvore de processos. O ganho vem de hibernação, não da casca.

## Engine

**WebView2 Evergreen** — o runtime do sistema. A Microsoft atualiza, o engine nunca
envelhece, não pesa no instalador e não há republicação por CVE do Chromium.

## Stack

**C# / .NET 10 + WinForms**, pacote `Microsoft.Web.WebView2`. Barra de título nativa;
faixa de abas e toolbar desenhadas à mão (GDI+, double-buffered, tema escuro).

Enxugamento em `Directory.Build.props`: GC workstation não-concorrente,
`TieredPGO`, satélites de idioma só em pt-BR e en.

**`InvariantGlobalization` fica desligado de propósito.** Ligado, ele economiza
alguns MB de ICU, mas faz o WinForms estourar ao trocar o layout de teclado:
`InputLanguageChangedEventArgs` chama `CultureInfo.GetCultureInfo(1046)` e em modo
invariante não existe cultura nenhuma. Aconteceu em uso real. Há teste de
regressão travando a configuração.

A casca custa **~10 MB de working set privado**.

> Rust/Win32 foi avaliado e recusado: economizaria ~45 MB (6% com 10 abas) ao custo
> de 4 GB de toolchain e do único risco real de cronograma. A casca nunca foi onde a
> briga de RAM acontece.

## Containers

Perfis do WebView2 **sob uma única pasta de dados**, compartilhando um processo de
browser. A alternativa (uma pasta por container) sobe um runtime inteiro por conta.

- Uma janela, containers **por aba**, selo de cor na aba.
- `Ctrl+T` abre no container padrão; botão direito num link → "Abrir em container".
- Isola cookies, `localStorage`, `IndexedDB`, cache e autofill.
- Implementação: `webView.CreationProperties.ProfileName = "<container>"`.

Confirmado que o limite de conta do jogo é de armazenamento (janela anônima permite
a segunda conta), então containers resolvem.

## Hibernação

Onde a RAM é ganha.

- Aba em fundo **suspensa após 5 min** (`TrySuspendAsync`: congela, não descarta).
- `MemoryUsageTargetLevel = Low` em tudo que está em fundo.
- **Pin isenta a aba de hibernação pra sempre.**
- Lista de domínios **nunca-hibernar**, com o jogo dentro; abas do jogo nascem fixadas.
- Sem descarte-e-recarga na v1: perder progresso de jogo idle é pior que gastar 100 MB.

## Escopo da v1

**Dentro:** abas + containers, barra de endereço com busca, voltar/avançar/recarregar,
histórico, downloads, favoritos, DevTools, zoom por site, sessão restaurada, bloqueio
por domínio, hotkeys.

**Fora:** gerenciador de senhas próprio (o autofill do Edge por perfil já funciona),
sync, extensões, sintaxe ABP completa, modo leitura, tradução, barra de título
integrada, múltiplas janelas.

## Bloqueio

Por domínio, via `AddWebResourceRequestedFilter` — sem extensão, sem renderer extra.
Casa subdomínios subindo rótulo a rótulo (`pixel.ads.doubleclick.net` cai por
`doubleclick.net`, mas `naoads.test` não cai por `ads.test`). O documento principal
nunca é bloqueado: digitar um domínio da lista na barra tem de abrir a página.

Semente embutida com 55 rastreadores comuns. **Para cobertura de verdade**, baixe um
arquivo `hosts` (por exemplo o do StevenBlack) e salve como
`%LOCALAPPDATA%\Levhrium\blocklist.txt` — as duas listas são somadas. Arquivo
ausente ou ilegível não é erro. `0.0.0.0 localhost` é ignorado de propósito: toda
lista hosts real começa com essa linha.

Não para anúncio servido do domínio da própria página — limitação aceita.

## Hotkeys

| Atalho | Ação |
|---|---|
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | próxima aba à direita / à esquerda (**ordem visual**) |
| `Ctrl+W` | fecha a aba; na última, mantém a janela com aba em branco |
| `Ctrl+Shift+T` | reabre a última fechada, **restaurando container + URL + posição** (pilha de 25) |
| `Ctrl+T` | nova aba no container padrão |
| `Ctrl+L` | foca a barra de endereço |
| `F5` / `Ctrl+R` | recarrega |
| `Alt+←` / `Alt+→` | voltar / avançar |
| `Ctrl+±` / `Ctrl+0` | zoom |
| `F12` | DevTools |
| `Ctrl+D` | favoritar / desfavoritar a página |
| `Ctrl+H` / `Ctrl+J` | menu da biblioteca aberto no histórico / nos downloads |

`AreBrowserAcceleratorKeysEnabled = false`: os atalhos são do Levhrium, não do Edge.

### Como a tecla chega até aqui

Há **dois caminhos de teclado**, e isso não é detalhe de implementação — é a
causa de um bug que chegou ao uso real:

- **Foco na casca** (barra de endereço, faixa de abas): a tecla entra pela fila de
  mensagens do WinForms e o `ProcessCmdKey` do formulário é chamado.
- **Foco dentro da página**: o WebView2 roda **em outro processo**. O `WM_KEYDOWN`
  vai para a janela do Chromium e **nunca aparece na nossa fila** — o
  `ProcessCmdKey` não é chamado. A tecla só chega ao host pelo evento `KeyDown`
  do próprio controle WebView2, que o controle levanta a partir do tratamento de
  acelerador. É preciso marcar `Handled` e `SuppressKeyPress`, senão o atalho
  também vaza para o site.

Os dois caminhos chamam a mesma tabela (`TryHandleHotkey`). O evento bloqueia o
processo do browser enquanto roda, então o handler só reconhece a tecla e enfileira
a ação na UI.

**O que os testes conseguem e não conseguem provar:**

| Caminho | Como é testado |
|---|---|
| Tabela de atalhos | named pipe chama `TryHandleHotkey` — 8 testes |
| Tecla comum chega ao site | Playwright digita num campo — 1 teste |
| **Tecla física com foco na página** | **não automatizado** |

O último não tem teste porque teclas enviadas pelo Playwright entram pelo CDP
direto no renderer e **não passam pelo tratamento de acelerador**: um teste de
`Ctrl+T` por ali falha mesmo com o atalho funcionando. E `SendInput` exige a
janela em primeiro plano, o que um processo em segundo plano não consegue obter
no Windows. **Esse caminho precisa de verificação manual**: abrir o navegador,
clicar na página e apertar `Ctrl+T`.

## Disco

`%LOCALAPPDATA%\Levhrium\` com:

- `WebView2\` — pasta única de dados, perfis como subpastas (um processo de browser).
- `state.json` — abas, containers e suas cores, aba ativa, favoritos, histórico
  (até 5000), downloads, zoom por host e pilha de fechadas. Gravado em temporário e
  movido por cima: um desligamento no meio da escrita não perde a sessão.
  Arquivo corrompido faz o navegador começar limpo, não recusar abrir.
- `blocklist.txt` — opcional, formato hosts.

Sem SQLite: uma dependência a menos e menos RAM.

## Estrutura

- **`src/Levhrium.Core`** — containers, perfis, sessão, política de hibernação,
  blocklist, pilha de fechadas. Zero WinForms, zero WebView2. Testável sem abrir janela.
- **`src/Levhrium.App`** — WinForms, abas, desenho, hotkeys, cola com o WebView2.

## Testes

Todo critério de "pronto" dos marcos vira teste, nunca verificação manual.

| Camada | Ferramenta | Cobre |
|---|---|---|
| Unitário | xUnit sobre `Levhrium.Core` | hibernação, pilha de fechadas, containers, blocklist, sessão |
| Integração | **Playwright/C# via CDP** | isolamento entre containers, bloqueio, suspensão/retomada |
| UI/E2E | **named pipe de automação** + baseline de screenshot | abas, hotkeys, selo, barra de endereço |

O Playwright dirige o DOM dentro das abas, mas **não alcança a casca** (GDI+ não é DOM,
e faixa de abas custom-desenhada não existe na árvore de UI Automation). Daí o pipe.

### Segurança dos hooks de teste

Não negociável, porque porta CDP aberta entrega cookies e sessões a qualquer processo local:

- CDP e named pipe existem **somente** com `--test-mode`.
- `--test-mode` só funciona em builds com `LEVHRIUM_TESTHOOKS` (Debug). No Release a
  flag é ignorada e o código nem é compilado.
- Porta **efêmera sorteada**, nunca 9222 fixa. O teste a descobre pelo arquivo de
  handshake (`--test-handshake`).
- Named pipe com ACL restrita ao usuário atual.
- Pasta de dados descartável em `%TEMP%`: nenhum teste encosta nos dados reais.

**A trava precisa ser rodada em Release para ser verificada.** `dotnet test` usa
Debug, onde o teste da trava fica compilado para fora. Rode também:

```
dotnet test tests/Levhrium.Core.Tests -c Release
```

Verificado também no binário: `Levhrium.dll` de Release não contém nenhum dos
símbolos `remote-debugging-port`, `AutomationServer`, `Handshake`, `get-state` ou
`hibernate-tick`, que estão todos presentes no de Debug.

### Fixtures locais, nunca o jogo real

Kestrel sobe junto do teste servindo páginas determinísticas. Testar contra
`poke.sigmaproject.online` exigiria credenciais no repositório, quebraria quando o jogo
mudasse e marteleria um serviço de terceiro a cada `dotnet test`. O jogo real entra só
na validação manual.

## Marcos

| M | Entrega | Pronto quando | Estado |
|---|---|---|---|
| **M1** | Janela + 1 aba + barra de endereço + navegação | o jogo abre e navega | ✅ |
| **M2** | Abas + containers + selo + hotkeys | **duas contas logadas lado a lado** | ✅ |
| **M3** | Hibernação + pin + nunca-hibernar + `MemoryUsageTargetLevel` | 10 abas gastam menos que o Chrome com 10, **medido** | ✅ 538 MB × 1270 MB |
| **M4** | Bloqueio por domínio | requisições bloqueadas, contador visível | ✅ 10 bloqueios em bbc.com/news |
| **M5** | Sessão, histórico, favoritos, downloads, zoom por site | fecha e reabre com tudo no lugar | ✅ |
| **M6** | Ícone, instalador e atualização pelo navegador | amigo instala e atualiza sem ajuda | ✅ |

**v1 completa.** M2 era a meta real do projeto; M3, a meta da métrica.

## Distribuição

- **Instalação por usuário** em `%LOCALAPPDATA%\Programs\Levhrium`, sem admin.
  Não é só conveniência: é o que deixa o botão Atualizar trocar os arquivos sem
  pedir elevação a cada vez.
- **Publicação self-contained** (~112 MB em disco, 35 MB de instalador): quem
  recebe não precisa instalar o .NET antes.
- **O instalador resolve a dependência**: checa o registro do runtime WebView2 e,
  se faltar, baixa e roda o bootstrapper Evergreen da Microsoft.
- **Atualização** em *Configurações → Verificar atualizações*: lê a API de
  releases do GitHub, compara a tag (`v1.2.3`) com a versão em execução, baixa o
  instalador anexado e o executa em modo silencioso. A comparação de versões e a
  leitura da API ficam no núcleo, testadas sem rede.
- **O instalador não é assinado.** O SmartScreen avisa na primeira execução. Um
  certificado de assinatura resolveria, mas é pago.

## Privacidade do repositório

O repositório é público. O histórico foi reescrito para trocar o e-mail pessoal
pelo `@users.noreply.github.com` do GitHub, e varrido: zero ocorrências do e-mail,
do usuário do Windows ou de caminhos locais em qualquer objeto do git.
Arquivos de ferramentas de IA e de sessão estão no `.gitignore`.

## Riscos conhecidos

1. **Nem toda aba de fundo aceita congelar.** Na medição do M3, 6 das 9 abas de
   fundo congelaram — o WebView2 recusa `TrySuspend` em algumas situações. A
   política tenta de novo a cada tique. Se isso incomodar, o próximo passo é
   descarte agressivo (recarregar ao voltar), nunca para abas fixadas.
2. **Bloqueio por domínio não para anúncio de primeira parte.** Aceito.
3. **Baseline de screenshot é frágil** (DPI, fonte, tema) — usar em poucos casos.
4. **Polimento visual depende de olho humano.** Screenshot diz se quebrou, não se
   ficou bom.
5. **A barra de título escura não foi confirmada visualmente.** Usa a API suportada
   (`Application.SetColorMode`), mas `PrintWindow` desenha a moldura pelo tema
   legado e não serve de prova.
6. **Runtime WebView2 em outras máquinas**: presente aqui; Win10 sem Edge atualizado
   precisaria do bootstrapper. Só importa se houver distribuição.
7. **Uma janela só.** Decidido na v1; múltiplas janelas dobrariam a complexidade
   de sessão sem servir ao caso de uso.

## Rodando

```
dotnet build -c Debug
dotnet test                 # 101 unitários + 40 de integração/UI
dotnet run --project src/Levhrium.App
dotnet run --project src/Levhrium.App -- --url https://poke.sigmaproject.online/play
```

Opções de linha de comando:

| Flag | Efeito |
|---|---|
| `--url <url>` | abre nessa URL em vez de restaurar a sessão |
| `--hibernate-after <s>` | segundos de ociosidade antes de hibernar (0 desliga) |
| `--blocklist <arquivo>` | arquivo hosts extra |
| `--test-mode` | **só em build Debug**: abre CDP e named pipe de automação |
| `--test-handshake <arquivo>` | onde publicar porta CDP e nome do pipe |
| `--test-data-dir <pasta>` | pasta de dados fixa; só com `--test-mode` |
