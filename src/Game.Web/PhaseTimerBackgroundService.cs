using Game.Engine;

namespace Game.Web;

/// <summary>
/// Внешний серверный таймер-сервис, которого ждала <see cref="GameSessionState"/> ещё с Блока 8.1
/// (SPEC §4, §11) — раз в секунду проверяет, не истекло ли время текущей фазы, и переводит сессию
/// дальше через уже готовую оркестровку <see cref="PhaseAutoAdvancer.TryAdvance"/>. Вся логика «что
/// именно делать» — в Game.Engine (чистая, детерминированная, протестированная отдельно); здесь —
/// только периодичность опроса и потокобезопасность записи в общую <see cref="GameSessionHost.Session"/>.
/// </summary>
public sealed class PhaseTimerBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly GameSessionHost _host;
    private readonly ILogger<PhaseTimerBackgroundService> _logger;

    public PhaseTimerBackgroundService(GameSessionHost host, ILogger<PhaseTimerBackgroundService> logger)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(logger);

        _host = host;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            bool acted;
            TurnPhase phaseAfter;
            int turnAfter;

            lock (_host.SyncRoot)
            {
                if (_host.Session is null)
                {
                    continue;
                }

                acted = PhaseAutoAdvancer.TryAdvance(_host.Session, DateTimeOffset.UtcNow, Random.Shared);
                phaseAfter = _host.Session.State.CurrentPhase;
                turnAfter = _host.Session.State.CurrentTurn;
            }

            if (acted)
            {
                _logger.LogInformation("Сессия переведена по таймеру: ход {Turn}, фаза {Phase}.", turnAfter, phaseAfter);
            }
        }
    }
}
