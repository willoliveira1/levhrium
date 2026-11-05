<div align="center">

<img src="build/levhrium-256.png" width="128" alt="Levhrium">

# Levhrium

**Navegador Chromium leve para Windows, com containers por aba.**

Duas contas do mesmo site abertas ao mesmo tempo, na mesma janela —
e metade da RAM do Chrome.

</div>

---

## O problema que ele resolve

Muito site deixa você ter várias contas, mas só uma por navegador: a segunda
sessão derruba a primeira porque as duas dividem o mesmo cookie. A saída comum é
abrir uma janela anônima, outro navegador, ou outro perfil inteiro — e aí você
paga um processo de browser separado para cada conta.

O Levhrium resolve isso com **containers por aba**: cada aba pode viver num
perfil próprio, com cookies, `localStorage`, `IndexedDB` e cache separados, mas
todos compartilhando **um único processo de browser**. Um selo colorido na aba
diz qual conta é qual.

## RAM

Medido com as mesmas 10 abas, no mesmo Chromium 156, somando o working set
privado de toda a árvore de processos:

| | RAM |
|---|---|
| **Levhrium** | **538 MB** |
| Google Chrome | 1270 MB |

O ganho vem da **hibernação**: abas de fundo paradas há mais de 5 minutos são
congeladas (`TrySuspend`), não descartadas — voltam instantâneas e do ponto onde
pararam. Abas fixadas e domínios protegidos nunca hibernam, para não travar
progresso de jogos idle e afins.

## O que tem

- **Containers por aba** com selo colorido, criados sob demanda.
  Botão direito num link → *Abrir em container*.
- **Hibernação** configurável, com isenção por pin e por domínio.
- **Bloqueio de rastreadores** por domínio, direto no nativo — sem extensão e
  sem renderer extra. 55 domínios embutidos, e você pode somar um arquivo
  `hosts` completo.
- **Sessão restaurada** ao reabrir: abas, containers, cores, aba ativa.
- Favoritos, histórico, downloads, **zoom por site**, DevTools.
- **Atualização pelo próprio navegador**, em Configurações.
- Tema escuro, faixa de abas desenhada à mão.

### Atalhos

| Atalho | Ação |
|---|---|
| `Ctrl+T` | nova aba |
| `Ctrl+W` | fechar aba (na última, mantém a janela com aba em branco) |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | próxima aba à direita / à esquerda |
| `Ctrl+Shift+T` | reabrir a última fechada, **no container original** |
| `Ctrl+L` | focar a barra de endereço |
| `F5` / `Ctrl+R` | recarregar |
| `Alt+←` / `Alt+→` | voltar / avançar |
| `Ctrl+±` / `Ctrl+0` | zoom (lembrado por site) |
| `Ctrl+D` | favoritar / desfavoritar |
| `Ctrl+H` / `Ctrl+J` | histórico / downloads |
| `F12` | DevTools |

## Instalando

Baixe o instalador mais recente em **[Releases](../../releases)** e execute.

- Instala **por usuário**, sem pedir admin.
- Se o **runtime WebView2** não estiver na máquina, o instalador baixa e instala
  sozinho.
- Não precisa de .NET instalado: o aplicativo é self-contained.
- Requer **Windows 10 1809 (build 17763) ou mais novo, 64 bits**.

> O instalador não é assinado digitalmente, então o Windows SmartScreen vai
> avisar na primeira execução. *Mais informações* → *Executar assim mesmo*.

### Bloqueio de rastreadores mais agressivo

A lista embutida cobre os rastreadores mais comuns. Para cobertura de verdade,
baixe um arquivo no formato `hosts` (o do [StevenBlack](https://github.com/StevenBlack/hosts)
funciona bem) e salve como:

```
%LOCALAPPDATA%\Levhrium\blocklist.txt
```

As duas listas são somadas. *Configurações → Abrir pasta de dados* leva direto lá.

## Compilando

Precisa do [.NET 10 SDK](https://dotnet.microsoft.com/download). Para gerar o
instalador, também do [Inno Setup 6](https://jrsoftware.org/isinfo.php)
(`winget install JRSoftware.InnoSetup`).

```powershell
dotnet build -c Release
dotnet test                                   # 154 unitários + 49 de integração/UI/visual
dotnet test tests/Levhrium.Core.Tests -c Release   # trava de segurança dos hooks de teste
.\build.ps1                                   # publica e gera dist/Levhrium-Setup-<versão>.exe
```

## Como é por dentro

- **`src/Levhrium.Core`** — containers, sessão, política de hibernação, lista de
  bloqueio, biblioteca, versionamento. Zero WinForms, zero WebView2: roda em
  teste sem abrir janela.
- **`src/Levhrium.App`** — WinForms, faixa de abas em GDI+, e a cola com o
  WebView2.
- **`tools/IconGen`** — gera o ícone a partir de um PNG de origem.

O motor é o **WebView2 Evergreen**, o runtime Chromium que já vem com o Windows
e que a Microsoft mantém atualizado. O Levhrium não embarca nem versiona
Chromium: correções de segurança do engine chegam sozinhas.

Decisões de projeto, medições e limitações conhecidas estão em
[ESPECIFICACAO.md](ESPECIFICACAO.md).

## Limitações conhecidas

- Uma janela só (abas, não janelas).
- Bloqueio por domínio não para anúncio servido pelo domínio da própria página.
- Sem extensões, sem sincronização em nuvem.
- Nem toda aba de fundo aceita congelar: o WebView2 recusa `TrySuspend` em
  algumas situações, e a política tenta de novo a cada ciclo.
