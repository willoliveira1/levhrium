using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Levhrium.E2E.Tests;

/// <summary>
/// Servidor local com paginas deterministicas. Os testes nunca batem em
/// poke.sigmaproject.online: exigiria credenciais no repositorio, quebraria quando
/// o jogo mudasse e marteleria um servico de terceiro em cada execucao.
/// </summary>
public sealed class FixtureServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FixtureServer(WebApplication app, string baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    /// <summary>Endereco raiz, com porta sorteada pelo sistema.</summary>
    public string BaseAddress { get; }

    public string Url(string relativePath) => BaseAddress.TrimEnd('/') + "/" + relativePath.TrimStart('/');

    public static async Task<FixtureServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();

        app.MapGet("/um", () => Page("Pagina Um", "<h1 id=\"titulo\">Pagina Um</h1>"));
        app.MapGet("/dois", () => Page("Pagina Dois", "<h1 id=\"titulo\">Pagina Dois</h1>"));

        // Fixture de container (M2): grava um identificador nos tres armazenamentos
        // que o jogo poderia usar para detectar sessao duplicada.
        app.MapGet("/conta", (HttpContext context) =>
        {
            var existing = context.Request.Cookies["levhrium-conta"];
            var id = existing ?? Guid.NewGuid().ToString("n")[..8];

            context.Response.Cookies.Append("levhrium-conta", id, new CookieOptions
            {
                HttpOnly = false,
                SameSite = SameSiteMode.Lax,
            });

            return Page(
                "Conta " + id,
                $"""
                <h1 id="titulo">Conta</h1>
                <p id="conta-id">{id}</p>
                <script>
                  localStorage.setItem('levhrium-conta', '{id}');
                  document.title = 'Conta ' + '{id}';
                </script>
                """);
        });

        // Fixture de hibernacao: um contador que so avanca enquanto a pagina
        // executa. Congelada, o titulo para; descartada, voltaria a zero.
        app.MapGet("/contador", () => Page(
            "Contador 0",
            """
            <h1 id="titulo">Contador</h1>
            <p id="valor">0</p>
            <script>
              let n = 0;
              setInterval(() => {
                n += 1;
                document.getElementById('valor').textContent = String(n);
                document.title = 'Contador ' + n;
              }, 200);
            </script>
            """));

        await app.StartAsync();

        var address = app.Urls.First();
        return new FixtureServer(app, address);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static IResult Page(string title, string body) => Results.Content(
        $"""
        <!doctype html>
        <html lang="pt-br">
          <head><meta charset="utf-8"><title>{title}</title></head>
          <body>{body}</body>
        </html>
        """,
        "text/html; charset=utf-8");
}
