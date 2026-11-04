namespace Levhrium.Core;

/// <summary>
/// Onde o Levhrium guarda dados. Uma unica pasta de dados do WebView2 para todos
/// os containers (os perfis entram como subpastas dela) -- e o que mantem um
/// processo de browser so, em vez de um por container.
/// </summary>
public sealed class AppPaths
{
    public const string ProductName = "Levhrium";

    private AppPaths(string rootFolder, bool isDisposable)
    {
        RootFolder = rootFolder;
        IsDisposable = isDisposable;
    }

    /// <summary>Raiz dos dados: %LOCALAPPDATA%\Levhrium em uso normal.</summary>
    public string RootFolder { get; }

    /// <summary>Pasta de dados do usuario do WebView2 (perfis dentro dela).</summary>
    public string WebView2Folder => Path.Combine(RootFolder, "WebView2");

    /// <summary>Sessao, favoritos, historico e pilha de abas fechadas.</summary>
    public string StateFile => Path.Combine(RootFolder, "state.json");

    /// <summary>Arquivo hosts opcional do usuario, somado a lista embutida.</summary>
    public string BlocklistFile => Path.Combine(RootFolder, "blocklist.txt");

    /// <summary>Verdadeiro so para as pastas temporarias de teste, que podem ser apagadas.</summary>
    public bool IsDisposable { get; }

    /// <summary>Pastas reais do usuario.</summary>
    public static AppPaths ForNormalUse()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductName);

        return new AppPaths(root, isDisposable: false);
    }

    /// <summary>
    /// Pasta descartavel em %TEMP%, usada somente em --test-mode. Garante que
    /// nenhum teste automatizado encoste nos dados reais de navegacao.
    /// </summary>
    public static AppPaths ForTests(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        var root = Path.Combine(Path.GetTempPath(), $"{ProductName}-test-{instanceId}");
        return new AppPaths(root, isDisposable: true);
    }

    /// <summary>
    /// Pasta de teste escolhida pelo chamador, que sobrevive ao fechamento do
    /// app. E assim que o teste de restauracao de sessao consegue abrir, fechar
    /// e reabrir sobre os mesmos dados.
    /// </summary>
    public static AppPaths ForTestFolder(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        return new AppPaths(root, isDisposable: false);
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootFolder);
        Directory.CreateDirectory(WebView2Folder);
    }

    /// <summary>Apaga a pasta, recusando-se a tocar em qualquer coisa que nao seja de teste.</summary>
    public void DeleteIfDisposable()
    {
        if (!IsDisposable || !Directory.Exists(RootFolder))
        {
            return;
        }

        try
        {
            Directory.Delete(RootFolder, recursive: true);
        }
        catch (IOException)
        {
            // O runtime do WebView2 pode ainda estar soltando handles; sobra em %TEMP%.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
