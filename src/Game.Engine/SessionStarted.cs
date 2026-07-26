using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Сессия начата: ход окончания разыгран жеребьёвкой в диапазоне пресета и зафиксирован в журнале
/// (SPEC §4) — это первая запись в истории сессии, точный ход окончания не сообщается игрокам.
/// Заодно регистрирует состав команд: по SPEC §9.6 регистрация происходит до старта таймера, так
/// что ростер уже известен целиком в момент, когда ведущий запускает сессию.
/// </summary>
public sealed record SessionStarted : Change<GameSessionState>
{
    /// <summary>Код пресета длительности, из диапазона которого был разыгран <see cref="EndTurn"/>.</summary>
    public required string PresetId { get; init; }

    /// <summary>Разыгранный ход окончания игры.</summary>
    public required int EndTurn { get; init; }

    /// <summary>Состав команд сессии.</summary>
    public required IReadOnlyList<TeamSpec> Teams { get; init; }

    public override void Apply(GameSessionState state)
    {
        state.PresetId = PresetId;
        state.EndTurn = EndTurn;
        state.CurrentTurn = 1;
        state.CurrentPhase = TurnPhase.Calculation;
        state.PhaseExtensionSeconds = TimeSpan.Zero;
        state.IsPaused = false;
        state.IsFinished = false;

        foreach (var spec in Teams)
        {
            var sector = state.Config.Sectors.First(s => s.Id == spec.SectorId);
            var team = new Team(spec.Id, spec.Name, sector);
            if (spec.StartingLoanAmount > 0)
            {
                team.TakeLoan(spec.StartingLoanAmount);
            }

            state.AddTeam(team);
        }
    }
}
