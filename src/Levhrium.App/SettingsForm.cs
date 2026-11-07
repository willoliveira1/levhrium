using System.Diagnostics;
using Levhrium.Core;

namespace Levhrium.App;

/// <summary>
/// Configuracoes do Levhrium, em cartoes.
///
/// O desenho antigo era rotulo e caixinha empilhados, sem agrupar nada: dava
/// para ler, mas nao dava para enxergar o que era assunto de quem. Agora cada
/// assunto e uma linha com ladrilho de icone, titulo, explicacao e o controle
/// que age sobre ele, do lado direito, onde a mao procura.
/// </summary>
internal sealed class SettingsForm : Form
{
    private const int Gutter = 24;
    private const int CardPadding = 18;
    private const int RowHeight = 34;

    private readonly AppPaths _paths;
    private readonly DomainBlocklist _blocklist;
    private readonly UpdateService _updates;
    private readonly Func<int> _readHibernateMinutes;
    private readonly Action<int> _writeHibernateMinutes;
    private readonly Action<bool> _writeStatusBar;

    private readonly Label _status;
    private readonly PillButton _check;
    private readonly ProgressBar _progress;
    private readonly NumericUpDown _hibernateMinutes;

    private UpdateCheckResult? _pending;

    /// <summary>Arquivos ja trocados no disco; falta so reiniciar.</summary>
    private bool _restartReady;

    internal SettingsForm(
        AppPaths paths,
        DomainBlocklist blocklist,
        UpdateService updates,
        Func<int> readHibernateMinutes,
        Action<int> writeHibernateMinutes,
        Func<bool> readStatusBar,
        Action<bool> writeStatusBar)
    {
        _paths = paths;
        _blocklist = blocklist;
        _updates = updates;
        _readHibernateMinutes = readHibernateMinutes;
        _writeHibernateMinutes = writeHibernateMinutes;
        _writeStatusBar = writeStatusBar;

        Text = "Configurações do " + AppPaths.ProductName;
        BackColor = Theme.WindowBackground;
        ForeColor = Theme.Foreground;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        const int cardWidth = 592;
        var y = 22;

        Controls.Add(new Label
        {
            Text = "Configurações",
            AutoSize = true,
            Location = new Point(Gutter, y),
            ForeColor = Theme.Foreground,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
        });

        y += 34;

        // ---- Cartao 1: versao, hibernacao, bloqueio ----

        var principal = new Card { Location = new Point(Gutter, y), Width = cardWidth };
        var linha = CardPadding - 2;

        var versaoTitulo = RowTitle($"{AppPaths.ProductName} {AppVersion.CurrentDisplay}");
        _status = RowSubtitle("Clique em verificar para procurar uma versão nova.");
        _check = new PillButton("Verificar", 104);
        _check.Click += async (_, _) => await OnCheckOrInstallAsync();

        AddRow(principal, linha, Glyphs.Reload, Theme.Accent, versaoTitulo, _status, _check);

        _progress = new ProgressBar
        {
            Location = new Point(CardPadding + 47, linha + RowHeight + 6),
            Width = cardWidth - CardPadding - 47 - CardPadding - 110,
            Height = 4,
            Style = ProgressBarStyle.Continuous,
            Visible = false,
        };
        principal.Controls.Add(_progress);

        linha += RowHeight + 16;
        principal.Controls.Add(new Divider
        {
            Location = new Point(CardPadding, linha),
            Width = cardWidth - (CardPadding * 2),
        });

        linha += 15;

        _hibernateMinutes = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 240,
            Value = Math.Clamp(_readHibernateMinutes(), 0, 240),
            Width = 46,
            BackColor = Theme.RowBackground,
            ForeColor = Theme.Foreground,
            BorderStyle = BorderStyle.None,
            TextAlign = HorizontalAlignment.Center,
            Font = new Font("Segoe UI", 10f),
        };
        _hibernateMinutes.ValueChanged += (_, _) => _writeHibernateMinutes((int)_hibernateMinutes.Value);

        // O NumericUpDown sem borda fica dentro de uma caixa funda arredondada:
        // o controle do WinForms nao tem canto nem fundo proprios.
        var campo = new SunkenRow { Size = new Size(58, 30) };
        campo.Controls.Add(_hibernateMinutes);
        _hibernateMinutes.Location = new Point(
            (campo.Width - _hibernateMinutes.Width) / 2,
            (campo.Height - _hibernateMinutes.Height) / 2);

        var minutos = new Panel { Size = new Size(92, 30), BackColor = Theme.CardBackground };
        minutos.Controls.Add(campo);
        minutos.Controls.Add(new Label
        {
            Text = "min",
            AutoSize = true,
            Location = new Point(64, 8),
            ForeColor = Theme.CardSubtitle,
        });

        AddRow(
            principal,
            linha,
            Glyphs.Moon,
            Theme.Purple,
            RowTitle("Hibernar abas paradas"),
            RowSubtitle("Abas fixadas e domínios protegidos nunca hibernam. Zero desliga."),
            minutos);

        linha += RowHeight + 16;
        principal.Controls.Add(new Divider
        {
            Location = new Point(CardPadding, linha),
            Width = cardWidth - (CardPadding * 2),
        });

        linha += 15;

        var pasta = new PillButton("Pasta", 92);
        pasta.Click += (_, _) => OpenDataFolder();

        AddRow(
            principal,
            linha,
            Glyphs.Shield,
            Theme.Success,
            RowTitle("Bloqueio de rastreadores"),
            RowSubtitle($"{_blocklist.Count} domínios carregados. Some um blocklist.txt na pasta de dados."),
            pasta);

        linha += RowHeight + 16;
        principal.Controls.Add(new Divider
        {
            Location = new Point(CardPadding, linha),
            Width = cardWidth - (CardPadding * 2),
        });

        linha += 15;

        var ligada = readStatusBar();
        var barra = new PillButton(ligada ? "Ligada" : "Desligada", 104);

        barra.Click += (_, _) =>
        {
            ligada = !ligada;
            barra.Text = ligada ? "Ligada" : "Desligada";
            _writeStatusBar(ligada);
        };

        AddRow(
            principal,
            linha,
            Glyphs.Gauge,
            Theme.Accent,
            RowTitle("Barra de status"),
            RowSubtitle("Mostra memória e CPU no rodapé. Custa 22px de altura da página."),
            barra);

        principal.Height = linha + RowHeight + CardPadding;
        Controls.Add(principal);

        ClientSize = new Size(cardWidth + (Gutter * 2), y + principal.Height + Gutter);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeTheme.ApplyDarkTitleBar(Handle);
    }

    /// <summary>
    /// Monta uma linha do cartao: ladrilho, titulo, explicacao e o controle que
    /// age, encostado na direita.
    /// </summary>
    private static void AddRow(
        Card card,
        int top,
        string glyph,
        Color accent,
        Label title,
        Label subtitle,
        Control action)
    {
        card.Controls.Add(new IconTile(glyph, accent) { Location = new Point(CardPadding, top) });

        var left = CardPadding + 47;
        var right = card.Width - CardPadding - action.Width;

        title.Location = new Point(left, top + 1);
        subtitle.Location = new Point(left, top + 19);
        subtitle.MaximumSize = new Size(right - left - 14, 0);

        action.Location = new Point(right, top + ((RowHeight - action.Height) / 2));

        card.Controls.Add(title);
        card.Controls.Add(subtitle);
        card.Controls.Add(action);
    }

    private static Label RowTitle(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Foreground,
        BackColor = Theme.CardBackground,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
    };

    private static Label RowSubtitle(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.CardSubtitle,
        BackColor = Theme.CardBackground,
        Font = new Font("Segoe UI", 8.5f),
    };

    /// <summary>
    /// O mesmo botao percorre os tres estados: verificar, instalar e reiniciar.
    /// </summary>
    private async Task OnCheckOrInstallAsync()
    {
        if (_restartReady)
        {
            UpdateService.RestartNow();
            return;
        }

        if (_pending is { HasUpdate: true })
        {
            await InstallAsync(_pending);
            return;
        }

        _check.Enabled = false;
        _status.Text = "Procurando...";
        _status.ForeColor = Theme.CardSubtitle;

        try
        {
            var result = await _updates.CheckAsync();
            _pending = result;

            if (result.Error is { Length: > 0 } error)
            {
                _status.Text = error;
                _status.ForeColor = Theme.CardSubtitle;
            }
            else if (result.HasUpdate)
            {
                _status.Text = $"Versão {result.PublishedVersion!.ToString(3)} disponível.";
                _status.ForeColor = Theme.Accent;
                _check.Text = "Baixar";
            }
            else
            {
                _status.Text = "Você está na versão mais nova.";
                _status.ForeColor = Theme.Success;
            }
        }
        finally
        {
            _check.Enabled = true;
        }
    }

    /// <summary>
    /// Tenta a atualizacao incremental e, se ela nao fechar por qualquer motivo,
    /// cai no instalador completo. A ordem importa: o incremental nao fecha o
    /// navegador, o instalador fecha.
    /// </summary>
    private async Task InstallAsync(UpdateCheckResult check)
    {
        _check.Enabled = false;
        _progress.Visible = true;
        _progress.Value = 0;
        _status.Text = "Baixando...";
        _status.ForeColor = Theme.CardSubtitle;

        var progress = new Progress<int>(percent => _progress.Value = Math.Clamp(percent, 0, 100));

        try
        {
            if (await _updates.PrepareIncrementalAsync(check, progress) is { } prepared)
            {
                _status.Text = "Aplicando...";

                var applied = UpdateService.ApplyIncremental(prepared);

                if (applied.Success)
                {
                    ShowRestartPrompt(prepared);
                    return;
                }
            }

            // Sem manifesto, pacote que nao cobre o plano, ou troca recusada.
            if (check.InstallerUrl is not { Length: > 0 } url)
            {
                _status.Text = "Não foi possível atualizar: a release não tem instalador.";
                _progress.Visible = false;
                _check.Enabled = true;
                return;
            }

            _progress.Value = 0;
            _status.Text = "Baixando o instalador completo...";

            var installer = await _updates.DownloadAsync(url, progress);

            _status.Text = "Fechando o Levhrium para instalar...";
            UpdateService.LaunchInstaller(installer);

            // O instalador nao consegue substituir arquivos em uso, entao o
            // navegador precisa sair. A sessao ja foi gravada ao fechar.
            await Task.Delay(500);
            Application.Exit();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            _status.Text = "Falhou ao baixar: " + ex.Message;
            _status.ForeColor = Theme.CardSubtitle;
            _progress.Visible = false;
            _check.Enabled = true;
        }
    }

    /// <summary>
    /// Os arquivos ja foram trocados no disco. O processo em execucao segue com
    /// o codigo antigo carregado em memoria, entao a versao nova so vale depois
    /// de reiniciar -- e por isso a mensagem fala em reiniciar, nao em instalar.
    /// </summary>
    private void ShowRestartPrompt(PreparedUpdate prepared)
    {
        var size = prepared.Plan.BytesToDownload / 1024d;
        var count = prepared.Plan.Download.Count;
        var arquivos = count == 1 ? "1 arquivo" : $"{count} arquivos";

        _progress.Visible = false;
        _status.Text =
            $"Versão {prepared.Version.ToString(3)} instalada: {arquivos}, {size:0} KB.";
        _status.ForeColor = Theme.Success;

        _restartReady = true;
        _check.Text = "Reiniciar";
        _check.Enabled = true;
    }

    private void OpenDataFolder()
    {
        _paths.EnsureCreated();

        Process.Start(new ProcessStartInfo
        {
            FileName = _paths.RootFolder,
            UseShellExecute = true,
        });
    }
}
