# Levhrium

Navegador Chromium leve para uso diário pessoal, com containers por aba.

## Objetivo

Dois requisitos mandam no projeto: **RAM baixa** e **múltiplas contas do mesmo site
simultaneamente** (caso motivador: duas contas do PokeIdle, que permite mais de uma
conta mas só uma por navegador).

- **Plataforma:** Windows 10 22H2 x64 e Windows 11 x64. Nenhuma concessão a portabilidade.
- **Métrica primária:** RAM. Secundária: abertura a frio. Disco: irrelevante.
- **Alvo:** o processo de browser do Chromium 156 custa ~100–130 MB sozinho; o
  ganho contra o Chrome aparece de 8 abas pra cima e vem de hibernação, não da casca.

## Engine

**WebView2 Evergreen** — o runtime do sistema. A Microsoft atualiza, o engine nunca
envelhece, não pesa no instalador e não há republicação por CVE do Chromium.

## Stack

**C# / .NET 10 + WinForms**, pacote `Microsoft.Web.WebView2`. Barra de título nativa;
faixa de abas e toolbar desenhadas à mão (GDI+, double-buffered, tema escuro).

Enxugamento em `Directory.Build.props`: `InvariantGlobalization`, GC workstation
não-concorrente, `TieredPGO`.

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

Por domínio: lista estilo `hosts` (~150k domínios num `HashSet`, ~8–12 MB) via
`AddWebResourceRequestedFilter`. ~10 MB gastos para economizar 50–200 MB em páginas
pesadas. Não para anúncio servido do domínio da própria página — limitação aceita.

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
| `Ctrl+D` / `Ctrl+H` / `Ctrl+J` | favoritar / histórico / downloads |

`AreBrowserAcceleratorKeysEnabled = false`: os atalhos são do Levhrium, não do Edge.

## Disco

`%LOCALAPPDATA%\Levhrium\` com `WebView2\` (pasta única, perfis como subpastas) e
`state.json` (abas, containers, favoritos, histórico, pilha de fechadas), gravado
atomicamente. Sem SQLite.

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

### Fixtures locais, nunca o jogo real

Kestrel sobe junto do teste servindo páginas determinísticas. Testar contra
`poke.sigmaproject.online` exigiria credenciais no repositório, quebraria quando o jogo
mudasse e marteleria um serviço de terceiro a cada `dotnet test`. O jogo real entra só
na validação manual.

## Marcos

| M | Entrega | Pronto quando | Estado |
|---|---|---|---|
| **M1** | Janela + 1 aba + barra de endereço + navegação | o jogo abre e navega | ✅ |
| **M2** | Abas + containers + selo + hotkeys | **duas contas logadas lado a lado** | — |
| **M3** | Hibernação + pin + nunca-hibernar + `MemoryUsageTargetLevel` | 10 abas gastam menos que o Chrome com 10, **medido** | — |
| **M4** | Bloqueio por domínio | requisições bloqueadas, contador visível | — |
| **M5** | Sessão, histórico, favoritos, downloads, zoom por site | fecha e reabre com tudo no lugar | — |

M2 é a meta real do projeto; M3 é a meta da métrica.

## Riscos conhecidos

1. **O alvo de RAM é estimativa até o M3.** Se 10 abas não ganharem do Chrome, a
   resposta é descarte agressivo (M3b), não reescrever a casca.
2. **Bloqueio por domínio não para anúncio de primeira parte.** Aceito.
3. **Baseline de screenshot é frágil** (DPI, fonte, tema) — usar em poucos casos.
4. **Polimento visual depende de olho humano.** Screenshot diz se quebrou, não se
   ficou bom.
5. **Runtime WebView2 em outras máquinas**: presente aqui; Win10 sem Edge atualizado
   precisaria do bootstrapper. Só importa se houver distribuição.

## Rodando

```
dotnet build -c Debug
dotnet test                 # 30 unitários + 12 de integração/UI
dotnet run --project src/Levhrium.App -- --url https://poke.sigmaproject.online/play
```
