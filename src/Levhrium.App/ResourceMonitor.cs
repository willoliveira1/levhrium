using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Levhrium.Core;

namespace Levhrium.App;

/// <summary>
/// Mede quanta memória e CPU o Levhrium inteiro está usando — o processo da
/// casca mais todos os do WebView2 que ele gerou.
///
/// Mede o working set PRIVADO, não o total: páginas compartilhadas entre os
/// processos do Chromium seriam contadas uma vez por processo e o número
/// sairia muito acima da verdade. É a mesma métrica com que o projeto se
/// comparou ao Chrome, então tem de continuar sendo a mesma aqui.
///
/// Medir custa CPU, e seria irônico gastar CPU para mostrar CPU: lê a cada 2s,
/// redescobre a árvore de processos a cada 10s, e para de ler quando a janela
/// está minimizada.
/// </summary>
internal sealed class ResourceMonitor : IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DiscoveryInterval = TimeSpan.FromSeconds(10);

    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly Func<bool> _shouldSample;

    private int[] _pids = [];
    private DateTimeOffset _discoveredAt = DateTimeOffset.MinValue;
    private TimeSpan _lastProcessorTime = TimeSpan.Zero;
    private DateTimeOffset _lastSampleAt = DateTimeOffset.MinValue;

    internal ResourceMonitor(Func<bool> shouldSample)
    {
        _shouldSample = shouldSample;
        _timer.Interval = (int)SampleInterval.TotalMilliseconds;
        _timer.Tick += (_, _) => Sample();
    }

    internal event EventHandler<ResourceSample>? Sampled;

    internal void Start()
    {
        if (!_timer.Enabled)
        {
            // Primeira leitura imediata, para a barra não nascer vazia.
            Sample();
            _timer.Start();
        }
    }

    internal void Stop() => _timer.Stop();

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }

    private void Sample()
    {
        if (!_shouldSample())
        {
            return;
        }

        var agora = DateTimeOffset.UtcNow;

        if (agora - _discoveredAt >= DiscoveryInterval)
        {
            _pids = Descendants(Environment.ProcessId);
            _discoveredAt = agora;
        }

        long bytes = 0;
        var processorTime = TimeSpan.Zero;
        var contados = 0;

        foreach (var pid in _pids)
        {
            if (ReadPrivateWorkingSet(pid) is { } privados)
            {
                bytes += privados;
                contados++;
            }

            try
            {
                using var process = Process.GetProcessById(pid);
                processorTime += process.TotalProcessorTime;
            }
            catch (ArgumentException)
            {
                // Processo morreu entre a descoberta e a leitura.
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }

        var cpu = _lastSampleAt == DateTimeOffset.MinValue
            ? 0
            : ResourceMath.CpuPercent(
                processorTime - _lastProcessorTime,
                agora - _lastSampleAt,
                Environment.ProcessorCount);

        _lastProcessorTime = processorTime;
        _lastSampleAt = agora;

        Sampled?.Invoke(this, new ResourceSample
        {
            Bytes = bytes,
            CpuPercent = cpu,
            Processes = contados,
        });
    }

    /// <summary>
    /// O processo e tudo o que desceu dele. Usa o instantâneo do Toolhelp em
    /// vez de WMI: a consulta de WMI custa mais do que tudo o que estamos
    /// tentando medir, e o Toolhelp não precisa de pacote nenhum.
    /// </summary>
    private static int[] Descendants(int root)
    {
        var filhos = new Dictionary<int, List<int>>();
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);

        if (snapshot == IntPtr.Zero || snapshot == InvalidHandle)
        {
            return [root];
        }

        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };

            if (!Process32First(snapshot, ref entry))
            {
                return [root];
            }

            do
            {
                if (!filhos.TryGetValue((int)entry.ParentProcessId, out var lista))
                {
                    lista = [];
                    filhos[(int)entry.ParentProcessId] = lista;
                }

                lista.Add((int)entry.ProcessId);
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        var encontrados = new List<int> { root };
        var fila = new Queue<int>();
        fila.Enqueue(root);

        while (fila.Count > 0)
        {
            if (!filhos.TryGetValue(fila.Dequeue(), out var lista))
            {
                continue;
            }

            foreach (var filho in lista)
            {
                // Um PID reciclado pode apontar para o pai e virar um ciclo.
                if (encontrados.Contains(filho))
                {
                    continue;
                }

                encontrados.Add(filho);
                fila.Enqueue(filho);
            }
        }

        return [.. encontrados];
    }

    /// <summary>
    /// Working set privado do processo. Nulo quando não deu para ler -- o
    /// processo pode ter morrido, ou ser de outro usuário.
    /// </summary>
    private static long? ReadPrivateWorkingSet(int pid)
    {
        var handle = OpenProcess(QueryLimitedInformation | VmRead, false, pid);

        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var counters = new ProcessMemoryCountersEx2
            {
                Size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx2>(),
            };

            // EX2 traz o working set privado pronto, a partir do Windows 10.
            if (GetProcessMemoryInfo(handle, ref counters, counters.Size) &&
                counters.PrivateWorkingSetSize > 0)
            {
                return (long)counters.PrivateWorkingSetSize;
            }

            // Sem EX2: PrivateUsage é memória comprometida, não working set.
            // Fica acima do real, mas é a melhor aproximação disponível.
            return counters.PrivateUsage > 0 ? (long)counters.PrivateUsage : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private const uint SnapProcess = 0x00000002;
    private const uint QueryLimitedInformation = 0x1000;
    private const uint VmRead = 0x0010;

    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx2
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
        public nuint PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [DllImport("psapi.dll", SetLastError = true, EntryPoint = "GetProcessMemoryInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCountersEx2 counters, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
