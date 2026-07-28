using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Сессия начата: ход окончания разыгран жеребьёвкой в диапазоне пресета и зафиксирован в журнале
/// (SPEC §4) — это первая запись в истории сессии, точный ход окончания не сообщается игрокам.
/// Заодно регистрирует состав команд: по SPEC §9.6 регистрация происходит до старта таймера, так
/// что ростер уже известен целиком в момент, когда ведущий запускает сессию. Заодно публикует
/// котировки рынка первого хода (Блок 6.1) — они нужны с самого начала фазы решений, до того как
/// для этого хода вообще будет вызван <see cref="GameSession.RunTick"/>.
/// </summary>
public sealed record SessionStarted : Change<GameSessionState>
{
    /// <summary>Код пресета длительности, из диапазона которого был разыгран <see cref="EndTurn"/>.</summary>
    public required string PresetId { get; init; }

    /// <summary>Разыгранный ход окончания игры.</summary>
    public required int EndTurn { get; init; }

    /// <summary>
    /// Контент-хеш GameConfig, с которым была начата сессия (см. <see cref="Game.Config.Loading.GameConfigHash"/>).
    /// Привязывает журнал к своему конфигу: при восстановлении хеш сверяется с фактически поданным
    /// конфигом, и дрейф/подмена обнаруживаются.
    /// </summary>
    public required string ConfigHash { get; init; }

    /// <summary>Состав команд сессии.</summary>
    public required IReadOnlyList<TeamSpec> Teams { get; init; }

    public override void Apply(GameSessionState state)
    {
        // Guard целостности, а не бизнес-валидация: на нормальном старте хеш взят из этого же
        // конфига и всегда совпадает; несовпадение возможно только при попытке доиграть журнал
        // поверх другого/подменённого конфига — это тот же класс защиты, что и VerifyIntegrity.
        if (ConfigHash != state.Config.ContentHash)
        {
            throw new InvalidOperationException(
                "Session journal was created with a different GameConfig than the one supplied for replay " +
                $"(expected content hash '{ConfigHash}', got '{state.Config.ContentHash}').");
        }

        state.ConfigHash = ConfigHash;
        state.PresetId = PresetId;
        state.EndTurn = EndTurn;
        state.CurrentTurn = 1;
        state.CurrentPhase = TurnPhase.Calculation;
        state.PhaseExtensionSeconds = TimeSpan.Zero;
        state.IsPaused = false;
        state.IsFinished = false;
        state.EmergencyPurchaseEnabled = state.Config.Raw.FeatureFlags.EmergencyPurchaseEnabled;

        foreach (var spec in Teams)
        {
            var sector = state.Config.Sectors.First(s => s.Id == spec.SectorId);
            var team = new Team(spec.Id, spec.Name, sector);
            state.AddTeam(team);
        }

        // Рынок первого хода публикуется прямо здесь, а не первым RunTick: до RunTick для хода 1
        // ещё дойдёт очередь (расчёт — отдельный, не автоматический шаг, см. GameSession.RunTick),
        // а решения (в т.ч. аварийная закупка) уже разрешены с фазы расчёта первого хода.
        var marketUpdate = MarketCalculator.Calculate(state.CurrentTurn, state.Config.Raw.Economy);
        state.Market.ReplaceQuotes(marketUpdate.Quotes, marketUpdate.ElectricityPrice);
    }
}
