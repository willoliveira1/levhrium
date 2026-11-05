using System.Drawing;
using System.Drawing.Imaging;

namespace Levhrium.E2E.Tests;

/// <summary>
/// Baseline visual da faixa de abas -- a camada combinada na rodada 6 e que
/// faltava: "hook de automacao como espinha dorsal, baseline de screenshot em
/// poucos casos".
///
/// Poucos casos de proposito: comparacao de pixel e fragil a DPI, fonte e tema,
/// entao so cobre o que nenhum outro teste alcanca -- que a faixa de abas
/// realmente desenha o selo de cor do container e a marca de aba fixada.
///
/// A referencia e gerada na primeira execucao e precisa ser conferida a olho
/// antes de entrar no repositorio.
/// </summary>
[Collection("app")]
public sealed class VisualBaselineTests
{
    /// <summary>Fracao de pixels que pode diferir antes de reprovar.</summary>
    private const double Tolerance = 0.02;

    /// <summary>
    /// So a barra de titulo e a faixa de abas. A barra de endereco fica de fora
    /// de proposito: ela mostra a porta do servidor de fixture, que e sorteada a
    /// cada execucao e faria a referencia mudar sozinha.
    /// </summary>
    private static readonly Rectangle Strip = new(0, 0, 1196, 70);

    [Fact]
    public async Task FaixaDeAbasComTresContainers()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs[0].Title == "Pagina Um", "primeira aba");
        await app.NewTabAsync("Conta 2", fixtures.Url("/dois"));
        await app.NewTabAsync("Trabalho", fixtures.Url("/um"));

        await app.WaitForStateAsync(
            s => s.Tabs.Count == 3 && s.Tabs.All(t => t.Title.Length > 0),
            "tres abas carregadas");

        await app.SelectTabAsync(1);
        await app.WaitForStateAsync(s => s.ActiveTabIndex == 1, "segunda aba ativa");
        await Task.Delay(700);

        AssertMatchesBaseline(app, "faixa-tres-containers");
    }

    [Fact]
    public async Task FaixaDeAbasComAbaFixada()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs[0].Title == "Pagina Um", "primeira aba");
        await app.NewTabAsync("padrao", fixtures.Url("/dois"));
        await app.WaitForStateAsync(s => s.Tabs.Count == 2, "duas abas");

        await app.PinTabAsync(0, pinned: true);
        await app.WaitForStateAsync(s => s.Tabs[0].Pinned, "primeira aba fixada");
        await Task.Delay(700);

        AssertMatchesBaseline(app, "faixa-aba-fixada");
    }

    private static void AssertMatchesBaseline(AppHarness app, string name)
    {
        using var captured = WindowCapture.Capture(app.Process, Strip);

        var baselineFile = Path.Combine(BaselineDirectory, name + ".png");

        if (!File.Exists(baselineFile))
        {
            Directory.CreateDirectory(BaselineDirectory);
            captured.Save(baselineFile, ImageFormat.Png);

            Assert.Fail(
                $"Referencia '{name}' nao existia e foi gerada em:{Environment.NewLine}" +
                $"  {baselineFile}{Environment.NewLine}" +
                "Confira a olho e versione o arquivo. Rode de novo para o teste valer.");
        }

        using var baseline = new Bitmap(baselineFile);
        var difference = WindowCapture.Difference(baseline, captured);

        if (difference <= Tolerance)
        {
            return;
        }

        // Guarda o que saiu, para dar para comparar lado a lado.
        var actualDirectory = Path.Combine(BaselineDirectory, "..", "screenshots-actual");
        Directory.CreateDirectory(actualDirectory);
        var actualFile = Path.Combine(actualDirectory, name + ".png");
        captured.Save(actualFile, ImageFormat.Png);

        Assert.Fail(
            $"A faixa de abas mudou em {difference:P1} dos pixels (limite {Tolerance:P0}).{Environment.NewLine}" +
            $"  referencia: {baselineFile}{Environment.NewLine}" +
            $"  saiu:       {actualFile}{Environment.NewLine}" +
            "Se a mudanca for proposital, apague a referencia e rode de novo para regerar.");
    }

    /// <summary>
    /// As referencias ficam na pasta do projeto de teste, nao na de build: elas
    /// sao versionadas junto com o codigo que descreve.
    /// </summary>
    private static string BaselineDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "Levhrium.E2E.Tests.csproj");

                if (File.Exists(candidate))
                {
                    return Path.Combine(directory.FullName, "baselines");
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Nao achei a pasta do projeto de teste.");
        }
    }
}
