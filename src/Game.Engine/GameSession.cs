using System.Text.Json;
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
    /// Начинает новую сессию: разыгрывает ход окончания в диапазоне пресета и пишет об этом первую
    /// запись в журнал. Сессия сразу открывается в фазе расчёта первого хода.
    /// </summary>
    public static GameSession Start(
        SessionPresetConfig preset,
        Random endTurnRandom,
        JsonSerializerOptions? serializerOptions = null,
        Func<DateTimeOffset>? clock = null)
    {
        var endTurn = SessionEndTurnDraw.Draw(preset, endTurnRandom);
        return StartWithEndTurn(preset.Id, endTurn, serializerOptions, clock);
    }

    /// <summary>Начинает сессию с уже известным ходом окончания (например, для тестов).</summary>
    public static GameSession StartWithEndTurn(
        string presetId,
        int endTurn,
        JsonSerializerOptions? serializerOptions = null,
        Func<DateTimeOffset>? clock = null)
    {
        var log = new EventLog<GameSessionState>(new GameSessionState(), serializerOptions, clock);
        log.Append(new SessionStarted { Id = Ulid.NewUlid(), PresetId = presetId, EndTurn = endTurn });

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
}
