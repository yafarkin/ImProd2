using System.Text.Json;
using Game.Config.Loading;
using Game.Config.Session;

namespace Game.Engine;

/// <summary>
/// Один прогон игры (AGENTS §2, правило 4 — состояние экземпляра, не статика; §5, терминология).
/// Тонкая обёртка над <see cref="EventLog{TState}"/> для <see cref="GameSessionState"/>: переводит
/// намерения («продлить фазу», «поставить на паузу») в конкретные <see cref="Change{TState}"/> и
/// проверяет бизнес-правила до записи в журнал — история, однажды воспроизводимая заново, не должна
/// сама бросать исключения валидации (AGENTS §2, правило 6).
/// </summary>
public sealed class GameSession
{
    private readonly EventLog<GameSessionState> _log;

    /// <summary>Живое состояние сессии.</summary>
    public GameSessionState State => _log.State;

    /// <summary>Полная история событий сессии.</summary>
    public IReadOnlyList<EventLogEntry<GameSessionState>> Entries => _log.Entries;

    /// <summary>Оборачивает уже существующий журнал (например, восстановленный durable-слоем).</summary>
    public GameSession(EventLog<GameSessionState> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <summary>
    /// Начинает новую сессию: разыгрывает ход окончания в диапазоне пресета и пишет об этом и о
    /// составе команд первую запись в журнал. Сессия сразу открывается в фазе расчёта первого хода.
    /// </summary>
    public static GameSession Start(
        ResolvedGameConfig config,
        SessionPresetConfig preset,
        IReadOnlyList<TeamSpec> teams,
        Random endTurnRandom,
        JsonSerializerOptions? serializerOptions = null,
        Func<DateTimeOffset>? clock = null)
    {
        var endTurn = SessionEndTurnDraw.Draw(preset, endTurnRandom);
        return StartWithEndTurn(config, preset.Id, endTurn, teams, serializerOptions, clock);
    }

    /// <summary>Начинает сессию с уже известным ходом окончания (например, для тестов).</summary>
    public static GameSession StartWithEndTurn(
        ResolvedGameConfig config,
        string presetId,
        int endTurn,
        IReadOnlyList<TeamSpec> teams,
        JsonSerializerOptions? serializerOptions = null,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(teams);

        foreach (var spec in teams)
        {
            if (spec.StartingLoanAmount > config.Raw.StartingConditions.MaxStartingLoanAmount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(teams), spec.StartingLoanAmount,
                    $"Team '{spec.Id}' requested a starting loan above the configured maximum " +
                    $"of {config.Raw.StartingConditions.MaxStartingLoanAmount}.");
            }
        }

        var log = new EventLog<GameSessionState>(new GameSessionState(config), serializerOptions, clock);
        log.Append(new SessionStarted { Id = Ulid.NewUlid(), PresetId = presetId, EndTurn = endTurn, Teams = teams });

        return new GameSession(log);
    }

    /// <summary>
    /// Переводит сессию к следующей фазе (или к следующему ходу, если завершалась фаза «завершение»),
    /// либо, если текущий ход — <see cref="GameSessionState.EndTurn"/>, помечает сессию завершённой.
    /// </summary>
    public EventLogEntry<GameSessionState> AdvancePhase(PhaseTransitionTrigger trigger)
    {
        if (State.IsFinished)
        {
            throw new InvalidOperationException("Cannot advance the phase of a session that has already finished.");
        }

        return _log.Append(new PhaseAdvanced { Id = Ulid.NewUlid(), Trigger = trigger });
    }

    /// <summary>Продлевает текущую фазу на <paramref name="by"/>.</summary>
    public EventLogEntry<GameSessionState> ExtendCurrentPhase(TimeSpan by)
    {
        if (by <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(by), by, "Phase extension must be positive.");
        }
        if (State.IsFinished)
        {
            throw new InvalidOperationException("Cannot extend a phase of a session that has already finished.");
        }

        return _log.Append(new PhaseExtended { Id = Ulid.NewUlid(), By = by });
    }

    /// <summary>Ставит сессию на паузу.</summary>
    public EventLogEntry<GameSessionState> Pause()
    {
        if (State.IsPaused)
        {
            throw new InvalidOperationException("Session is already paused.");
        }
        if (State.IsFinished)
        {
            throw new InvalidOperationException("Cannot pause a session that has already finished.");
        }

        return _log.Append(new SessionPaused { Id = Ulid.NewUlid() });
    }

    /// <summary>Снимает сессию с паузы.</summary>
    public EventLogEntry<GameSessionState> Resume()
    {
        if (!State.IsPaused)
        {
            throw new InvalidOperationException("Session is not paused.");
        }

        return _log.Append(new SessionResumed { Id = Ulid.NewUlid() });
    }

    /// <summary>
    /// Бросает, если решения команд сейчас недопустимы (любая фаза, кроме
    /// <see cref="TurnPhase.Decision"/>) — фазы расчёта и завершения read-only (SPEC §4).
    /// Действия команд (контракты, производство и т.д. из следующих блоков) обязаны вызывать этот
    /// метод перед записью своих событий.
    /// </summary>
    public void EnsureDecisionsAllowed()
    {
        if (State.CurrentPhase != TurnPhase.Decision)
        {
            throw new InvalidOperationException(
                $"Team decisions are not allowed during the '{State.CurrentPhase}' phase.");
        }
    }

    /// <summary>Проверяет целостность хеш-цепочки журнала сессии.</summary>
    public bool VerifyIntegrity() => _log.VerifyIntegrity();

    /// <summary>
    /// Прогоняет расчёт одного тика для всех команд: финансы, затем производство снизу вверх по
    /// уровню производимого материала (SPEC §4). Производство дописывается в журнал сразу по мере
    /// расчёта каждой фабрики — не собирается заранее единым списком, — чтобы фабрика более
    /// высокого уровня в этой же команде видела в складе выход, произведённый в этом же тике
    /// фабрикой более низкого уровня. Контракты/рынок/новости — заглушки (Фазы 5-6 ещё не
    /// реализованы). Не вызывается автоматически при переходе фаз — таймер-driven вызов из
    /// реального сеанса появится вместе с real-time слоем (Блок 8.2).
    /// </summary>
    public IReadOnlyList<EventLogEntry<GameSessionState>> RunTick()
    {
        if (State.CurrentPhase != TurnPhase.Calculation)
        {
            throw new InvalidOperationException(
                $"Cannot run a tick outside the '{TurnPhase.Calculation}' phase (currently '{State.CurrentPhase}').");
        }

        var appended = new List<EventLogEntry<GameSessionState>>();
        var config = State.Config;

        foreach (var team in State.Teams.Values.OrderBy(team => team.Id))
        {
            foreach (var change in TickFinanceStep.Run(team, config.Raw.StartingConditions, config.Raw.WorkerProductivity))
            {
                appended.Add(_log.Append(change));
            }

            foreach (var factory in team.Factories.OrderBy(f => f.SelectedRecipe.Output.Level).ThenBy(f => f.Id))
            {
                var result = ProductionCalculator.Calculate(
                    factory, team.Warehouse, config.Raw.WorkerProductivity, config.Raw.Rnd);

                appended.Add(_log.Append(new FactoryProduced
                {
                    Id = Ulid.NewUlid(),
                    TeamId = team.Id,
                    FactoryId = result.FactoryId,
                    CapacityLimitedOutputQuantity = result.CapacityLimitedOutputQuantity,
                    OutputQuantity = result.OutputQuantity,
                    ConsumedInputs = result.ConsumedInputs,
                }));
            }
        }

        return appended;
    }
}
