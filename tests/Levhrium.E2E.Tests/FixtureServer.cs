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
    private readonly RequestLog _log;

    private FixtureServer(WebApplication app, string baseAddress, RequestLog log)
    {
        _app = app;
        BaseAddress = baseAddress;
        _log = log;
    }

    /// <summary>
    /// Quantas vezes o servidor recebeu o caminho. Uma requisicao bloqueada nunca
    /// chega ate aqui -- e assim que o teste prova o bloqueio, em vez de confiar
    /// so no contador do proprio navegador.
    /// </summary>
    public int HitsFor(string path) => _log.Count(path);

    /// <summary>Endereco raiz, com porta sorteada pelo sistema.</summary>
    public string BaseAddress { get; }

    public string Url(string relativePath) => BaseAddress.TrimEnd('/') + "/" + relativePath.TrimStart('/');

    public static async Task<FixtureServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var log = new RequestLog();
        builder.Services.AddSingleton(log);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            log.Record(context.Request.Path.Value ?? string.Empty);
            await next();
        });

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

        // Fixture de bloqueio: a pagina vem de 127.0.0.1 e pede dois recursos --
        // um do mesmo host (permitido) e um de "localhost", host diferente que o
        // teste poe na lista de bloqueio. Mesmo servidor, hosts distintos.
        app.MapGet("/pagina-com-anuncio", (HttpContext context) =>
        {
            var port = context.Request.Host.Port ?? 80;

            return Page(
                "Pagina Com Anuncio",
                $"""
                <h1 id="titulo">Pagina Com Anuncio</h1>
                <img id="permitido" src="http://127.0.0.1:{port}/recurso-permitido.png">
                <img id="anuncio" src="http://localhost:{port}/recurso-anuncio.png">
                """);
        });

        // Fixture de teclado: um campo de texto, para provar que interceptar os
        // atalhos nao impede de digitar normalmente num site.
        app.MapGet("/digitar", () => Page(
            "Digitar",
            """
            <h1 id="titulo">Digitar</h1>
            <input id="campo" type="text" autofocus>
            """));

        app.MapGet("/recurso-permitido.png", () => Results.Bytes(TransparentPixel, "image/png"));
        app.MapGet("/recurso-anuncio.png", () => Results.Bytes(TransparentPixel, "image/png"));

        await app.StartAsync();

        var address = app.Urls.First();
        return new FixtureServer(app, address, log);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>PNG 1x1 transparente, o menor recurso valido possivel.</summary>
    private static readonly byte[] TransparentPixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private sealed class RequestLog
    {
        private readonly Dictionary<string, int> _hits = [];
        private readonly Lock _gate = new();

        public void Record(string path)
        {
            lock (_gate)
            {
                _hits[path] = _hits.GetValueOrDefault(path) + 1;
            }
        }

        public int Count(string path)
        {
            lock (_gate)
            {
                return _hits.GetValueOrDefault(path);
            }
        }
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
