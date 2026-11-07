namespace Levhrium.Core;

/// <summary>
/// Uma leitura de consumo: memória do conjunto de processos e fatia de CPU.
///
/// Memória é a métrica que justifica o Levhrium existir, então ela é a que
/// aparece primeiro na barra de status.
/// </summary>
public readonly record struct ResourceSample
{
    /// <summary>Soma do working set privado de todos os processos do navegador.</summary>
    public required long Bytes { get; init; }

    /// <summary>Fatia de CPU de 0 a 100, já dividida pelo número de núcleos.</summary>
    public required double CpuPercent { get; init; }

    /// <summary>Quantos processos entraram na conta; zero significa leitura falha.</summary>
    public required int Processes { get; init; }

    public bool IsEmpty => Processes == 0;
}

/// <summary>
/// Contas do medidor de consumo. Separadas da leitura em si para serem testadas
/// sem processo nenhum: a aritmética de CPU é fácil de errar por um fator de
/// número de núcleos, e o erro passa despercebido num número que muda sozinho.
/// </summary>
public static class ResourceMath
{
    /// <summary>
    /// Percentual de CPU a partir do tempo de processador gasto entre duas
    /// leituras.
    ///
    /// Divide pelo número de núcleos: sem isso, uma máquina de 8 núcleos
    /// mostraria 800% com tudo ocupado, que é verdade para o Windows e
    /// mentira para quem lê.
    /// </summary>
    public static double CpuPercent(TimeSpan processorTime, TimeSpan elapsed, int cores)
    {
        if (elapsed <= TimeSpan.Zero || cores <= 0 || processorTime <= TimeSpan.Zero)
        {
            return 0;
        }

        var percent = processorTime.TotalMilliseconds / (elapsed.TotalMilliseconds * cores) * 100;

        // Processo que nasceu entre as duas leituras pode render acima de 100.
        return Math.Clamp(percent, 0, 100);
    }

    /// <summary>
    /// "2,9 GB", "812 MB". Uma casa decimal só a partir de GB, onde ela ainda
    /// diz algo; em MB a fração seria ruído que muda a cada leitura.
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            return "--";
        }

        const double Mega = 1024d * 1024d;
        const double Giga = Mega * 1024d;

        if (bytes >= Giga)
        {
            return string.Create(
                System.Globalization.CultureInfo.GetCultureInfo("pt-BR"),
                $"{bytes / Giga:0.0} GB");
        }

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{Math.Round(bytes / Mega)} MB");
    }

    /// <summary>"7,7% CPU"; abaixo de 0,1% vira "0% CPU" em vez de poluir com casas.</summary>
    public static string FormatCpu(double percent) => string.Create(
        System.Globalization.CultureInfo.GetCultureInfo("pt-BR"),
        $"{(percent < 0.1 ? 0 : percent):0.#}% CPU");
}
