namespace Levhrium.Core;

/// <summary>
/// Um container: um perfil do WebView2 com identidade visual propria. O selo de cor
/// e o que faz voce enxergar, sem clicar, qual conta esta em qual aba.
/// </summary>
public sealed record Container
{
    public required string Name { get; init; }

    /// <summary>Cor do selo, em ARGB.</summary>
    public required int Color { get; init; }

    /// <summary>
    /// Nome do perfil passado ao WebView2. Perfis aceitam apenas letras, digitos,
    /// hifen, sublinhado e ponto, entao o nome exibido e saneado antes de virar perfil.
    /// </summary>
    public string ProfileName => Sanitize(Name);

    internal static string Sanitize(string name)
    {
        var sanitized = new string([.. name.Select(c =>
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-')]);

        return sanitized.Length == 0 ? "container" : sanitized;
    }
}

/// <summary>
/// Catalogo de containers. Cria sob demanda e distribui cores da paleta em ordem,
/// para que duas contas abertas em sequencia nunca saiam com a mesma cor.
/// </summary>
public sealed class ContainerRegistry
{
    public const string DefaultName = "padrao";

    /// <summary>Cores escolhidas para se distinguirem entre si no tema escuro.</summary>
    private static readonly int[] Palette =
    [
        unchecked((int)0xFFFF8A3D), // padrao: laranja
        unchecked((int)0xFF4C9AFF), // azul
        unchecked((int)0xFF46D17F), // verde
        unchecked((int)0xFFE05A8A), // rosa
        unchecked((int)0xFFB47BFF), // roxo
        unchecked((int)0xFFE5C04B), // amarelo
        unchecked((int)0xFF4ED6D6), // ciano
    ];

    private readonly Dictionary<string, Container> _containers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = [];

    public ContainerRegistry()
    {
        _ = GetOrCreate(DefaultName);
    }

    public Container Default => _containers[DefaultName];

    /// <summary>Containers na ordem em que foram criados.</summary>
    public IReadOnlyList<Container> All => [.. _order.Select(name => _containers[name])];

    public bool Exists(string name) => _containers.ContainsKey(name);

    public Container GetOrCreate(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var key = name.Trim();
        if (_containers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var container = new Container
        {
            Name = key,
            Color = Palette[_order.Count % Palette.Length],
        };

        _containers[key] = container;
        _order.Add(key);
        return container;
    }

    /// <summary>
    /// Repoe containers gravados mantendo as cores originais: um container que
    /// mudasse de cor ao reabrir o navegador derrubaria a unica pista visual de
    /// qual aba e qual conta.
    /// </summary>
    public void Restore(IEnumerable<PersistedContainer> containers)
    {
        ArgumentNullException.ThrowIfNull(containers);

        foreach (var saved in containers)
        {
            if (saved.Name is not { Length: > 0 } name || _containers.ContainsKey(name))
            {
                continue;
            }

            _containers[name] = new Container { Name = name, Color = ResolveColor(saved.Color) };
            _order.Add(name);
        }
    }

    /// <summary>
    /// Mantem a cor gravada, a nao ser que ela ja esteja ocupada; nesse caso
    /// pega a primeira livre da paleta.
    ///
    /// Duas contas com o mesmo selo apagam a unica pista de qual aba e qual --
    /// e e exatamente o que aconteceria com quem ja tinha um container laranja
    /// quando o padrao virou laranja na v1.0.6.
    /// </summary>
    private int ResolveColor(int saved)
    {
        var taken = _containers.Values.Select(c => c.Color).ToHashSet();

        if (!taken.Contains(saved))
        {
            return saved;
        }

        foreach (var color in Palette)
        {
            if (!taken.Contains(color))
            {
                return color;
            }
        }

        // Mais containers do que cores na paleta: repetir e inevitavel.
        return Palette[_order.Count % Palette.Length];
    }

    /// <summary>Containers no formato de gravacao.</summary>
    public IReadOnlyList<PersistedContainer> ToPersisted() =>
        [.. All.Select(c => new PersistedContainer { Name = c.Name, Color = c.Color })];

    /// <summary>
    /// Sugere um nome livre no formato "Conta N". Usado pelo botao de novo
    /// container, para nao exigir que voce invente nome a cada conta.
    /// </summary>
    public string SuggestName()
    {
        for (var i = 1; ; i++)
        {
            var candidate = "Conta " + i;
            if (!Exists(candidate))
            {
                return candidate;
            }
        }
    }
}
