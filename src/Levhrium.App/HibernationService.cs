using Levhrium.Core;

namespace Levhrium.App;

/// <summary>
/// Aplica a <see cref="HibernationPolicy"/> periodicamente. A decisao mora no
/// nucleo; aqui fica so a execucao: congelar, descongelar e pedir memoria baixa.
/// </summary>
internal sealed class HibernationService : IDisposable
{
    /// <summary>
    /// Com que frequencia a politica e avaliada. Curto o bastante para o teste
    /// com limiar de poucos segundos, barato o bastante para rodar o dia todo.
    /// </summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(2);

    private readonly TabSession _session;
    private readonly Func<HibernationPolicy> _policy;
    private readonly Func<Guid, BrowserTab?> _viewFor;
    private readonly System.Windows.Forms.Timer _timer;

    private bool _running;

    internal HibernationService(
        TabSession session,
        Func<HibernationPolicy> policy,
        Func<Guid, BrowserTab?> viewFor)
    {
        _session = session;
        _policy = policy;
        _viewFor = viewFor;

        _timer = new System.Windows.Forms.Timer { Interval = (int)TickInterval.TotalMilliseconds };
        _timer.Tick += (_, _) => _ = TickAsync();
        _timer.Start();
    }

    /// <summary>Quantas abas ja foram congeladas desde que o app subiu.</summary>
    internal int SuspendedCount { get; private set; }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }

    /// <summary>Avalia a politica agora, sem esperar o proximo tique.</summary>
    internal async Task TickAsync()
    {
        if (_running)
        {
            return;
        }

        _running = true;

        try
        {
            var now = DateTimeOffset.UtcNow;

            foreach (var tab in _session.Tabs.ToList())
            {
                var isActive = _session.Active?.Id == tab.Id;
                if (_viewFor(tab.Id) is not { } view)
                {
                    continue;
                }

                // Toda aba de fundo pede memoria baixa, inclusive as que nunca
                // hibernam: a fixada do jogo nao congela, mas pode soltar cache.
                view.SetMemoryUsageLow(!isActive);

                switch (_policy().Decide(tab.ToHibernationInfo(isActive), now))
                {
                    case HibernationAction.Suspend:
                        if (await view.TrySuspendAsync().ConfigureAwait(true))
                        {
                            tab.Suspended = true;
                            SuspendedCount++;
                        }

                        break;

                    case HibernationAction.Resume:
                        view.Resume();
                        tab.Suspended = false;
                        break;

                    case HibernationAction.None:
                    default:
                        break;
                }
            }
        }
        finally
        {
            _running = false;
        }
    }
}
