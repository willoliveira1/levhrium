using System.Text.Json;

namespace Levhrium.Core;

/// <summary>
/// Le e grava o <see cref="BrowserState"/>. Grava num temporario e move por
/// cima: um desligamento no meio da escrita nunca pode deixar o arquivo pela
/// metade e perder a sessao inteira.
/// </summary>
public sealed class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly Lock _gate = new();

    public StateStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <summary>
    /// Estado gravado, ou um estado vazio. Arquivo corrompido tambem devolve
    /// vazio: comecar limpo e melhor que recusar abrir o navegador.
    /// </summary>
    public BrowserState Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return new BrowserState();
            }

            try
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<BrowserState>(json, JsonOptions) ?? new BrowserState();
            }
            catch (JsonException)
            {
                return new BrowserState();
            }
            catch (IOException)
            {
                return new BrowserState();
            }
        }
    }

    public void Save(BrowserState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_path);
            if (directory is { Length: > 0 })
            {
                Directory.CreateDirectory(directory);
            }

            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
        }
    }
}
