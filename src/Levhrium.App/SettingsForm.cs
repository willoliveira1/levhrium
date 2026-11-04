using System.Diagnostics;
using Levhrium.Core;

namespace Levhrium.App;

/// <summary>
/// Configuracoes do Levhrium: versao e atualizacao, limiar de hibernacao e
/// atalhos para a pasta de dados e a lista de bloqueio.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly AppPaths _paths;
    private readonly DomainBlocklist _blocklist;
    private readonly UpdateService _updates;
    private readonly Func<int> _readHibernateMinutes;
    private readonly Action<int> _writeHibernateMinutes;

    private readonly Label _status;
    private readonly Button _check;
    private readonly ProgressBar _progress;
    private readonly NumericUpDown _hibernateMinutes;

    private UpdateCheckResult? _pending;

    internal SettingsForm(
        AppPaths paths,
        DomainBlocklist blocklist,
        UpdateService updates,
        Func<int> readHibernateMinutes,
        Action<int> writeHibernateMinutes)
    {
        _paths = paths;
        _blocklist = blocklist;
        _updates = updates;
        _readHibernateMinutes = readHibernateMinutes;
        _writeHibernateMinutes = writeHibernateMinutes;

        Text = "Configuracoes do " + AppPaths.ProductName;
        BackColor = Theme.WindowBackground;
        ForeColor = Theme.Foreground;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 400);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(20),
            BackColor = Theme.WindowBackground,
            AutoSize = false,
        };

        layout.Controls.Add(SectionTitle("Versao"));
        layout.Controls.Add(Caption($"{AppPaths.ProductName} {AppVersion.CurrentDisplay}"));

        _status = Caption("Clique em verificar para procurar uma versao nova.");
        layout.Controls.Add(_status);

        _progress = new ProgressBar
        {
            Width = 460,
            Height = 6,
            Style = ProgressBarStyle.Continuous,
            Visible = false,
            Margin = new Padding(0, 4, 0, 4),
        };
        layout.Controls.Add(_progress);

        _check = new Button
        {
            Text = "Verificar atualizacoes",
            Width = 190,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.FieldBackground,
            ForeColor = Theme.Foreground,
            Margin = new Padding(0, 6, 0, 16),
        };
        _check.FlatAppearance.BorderColor = Theme.Accent;
        _check.Click += async (_, _) => await OnCheckOrInstallAsync();
        layout.Controls.Add(_check);

        layout.Controls.Add(SectionTitle("Hibernacao"));
        layout.Controls.Add(Caption(
            "Abas de fundo sao congeladas depois deste tempo parado. Abas fixadas e" +
            Environment.NewLine +
            "dominios protegidos nunca hibernam. Zero desliga a hibernacao."));

        _hibernateMinutes = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 240,
            Value = Math.Clamp(_readHibernateMinutes(), 0, 240),
            Width = 80,
            BackColor = Theme.FieldBackground,
            ForeColor = Theme.Foreground,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 4, 0, 16),
        };
        _hibernateMinutes.ValueChanged += (_, _) => _writeHibernateMinutes((int)_hibernateMinutes.Value);
        layout.Controls.Add(_hibernateMinutes);

        layout.Controls.Add(SectionTitle("Bloqueio de rastreadores"));
        layout.Controls.Add(Caption(
            $"{_blocklist.Count} dominios carregados. Para cobertura maior, salve um" +
            Environment.NewLine +
            "arquivo no formato hosts como blocklist.txt na pasta de dados."));

        var openFolder = new Button
        {
            Text = "Abrir pasta de dados",
            Width = 190,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.FieldBackground,
            ForeColor = Theme.Foreground,
            Margin = new Padding(0, 6, 0, 0),
        };
        openFolder.FlatAppearance.BorderSize = 0;
        openFolder.Click += (_, _) => OpenDataFolder();
        layout.Controls.Add(openFolder);

        Controls.Add(layout);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeTheme.ApplyDarkTitleBar(Handle);
    }

    private static Label SectionTitle(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Foreground,
        Font = new Font("Segoe UI", 11f, FontStyle.Bold),
        Margin = new Padding(0, 0, 0, 6),
    };

    private static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(460, 0),
        ForeColor = Theme.ForegroundDim,
        Margin = new Padding(0, 0, 0, 4),
    };

    /// <summary>
    /// O mesmo botao serve para verificar e para instalar: depois de encontrar
    /// uma versao nova, ele vira "Baixar e instalar".
    /// </summary>
    private async Task OnCheckOrInstallAsync()
    {
        if (_pending is { HasUpdate: true, InstallerUrl: { Length: > 0 } url })
        {
            await InstallAsync(url);
            return;
        }

        _check.Enabled = false;
        _status.Text = "Procurando...";
        _status.ForeColor = Theme.ForegroundDim;

        try
        {
            var result = await _updates.CheckAsync();
            _pending = result;

            if (result.Error is { Length: > 0 } error)
            {
                _status.Text = error;
                _status.ForeColor = Theme.ForegroundDim;
            }
            else if (result.HasUpdate)
            {
                _status.Text = $"Versao {result.PublishedVersion!.ToString(3)} disponivel.";
                _status.ForeColor = Theme.Accent;
                _check.Text = "Baixar e instalar";
            }
            else
            {
                _status.Text = "Voce ja esta na versao mais nova.";
                _status.ForeColor = Theme.ForegroundDim;
            }
        }
        finally
        {
            _check.Enabled = true;
        }
    }

    private async Task InstallAsync(string url)
    {
        _check.Enabled = false;
        _progress.Visible = true;
        _progress.Value = 0;
        _status.Text = "Baixando...";

        try
        {
            var progress = new Progress<int>(percent => _progress.Value = Math.Clamp(percent, 0, 100));
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
            _status.ForeColor = Theme.ForegroundDim;
            _progress.Visible = false;
            _check.Enabled = true;
        }
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
