using Game.Engine;

namespace Game.Bots;

/// <summary>
/// Прогоняет одну или много партий силами простых ботов (Блок 7.2, BUILD_PLAN «Харнесс
/// балансировки») и собирает метрики: денежная масса и throughput по ходам, разброс итоговых
/// счётов — для калибровки GameConfig, не для игры вживую. Опционально
/// сверяет каждую команду с идеальным залом (Блок 7.3.5, <see cref="IdealHallCalculator"/>) —
/// Score(t)/X(t) той же ветки, посчитанным заранее, один раз на весь конфиг (X(t) не зависит от
/// стратегии ботов, пересчитывать его на каждую партию незачем).
/// </summary>
public static class BalancingHarness
{
    /// <summary>
    /// Прогоняет одну партию до конца и собирает её метрики. <paramref name="idealHall"/> —
    /// необязательный вход для сходимости (Блок 7.3.5); без него <see
    /// cref="TurnMetrics.AverageConvergence"/>/<see cref="SessionMetrics.FinalConvergenceBySector"/>
    /// остаются пустыми/<c>null</c>, а не бросают исключение — харнесс балансировки как таковой не
    /// требует X(t), это отдельная, накладываемая сверху проверка.
    /// </summary>
    public static SessionMetrics RunSession(
        GameSession session, IReadOnlyList<SimpleBot> bots, Random random, IdealHallResult? idealHall = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(bots);
        ArgumentNullException.ThrowIfNull(random);

        // Себестоимость не зависит от хода/рынка (см. doc-comment MaterialCostCalculator) — считаем
        // один раз на всю партию, не на каждый ход внутри колбэка.
        var materialCosts = MaterialCostCalculator.CalculateAll(session.State.Config);

        var turns = new List<TurnMetrics>();
        BotSessionRunner.RunToCompletion(session, bots, random, onTurnCompleted: appended =>
        {
            var totalCash = session.State.Teams.Values.Sum(team => team.Balance);
            var volumeSold = appended.Sum(entry => entry.Change is MaterialSoldToSystem sale ? sale.Volume : 0m);

            var allFactories = session.State.Teams.Values.SelectMany(team => team.Factories).ToList();
            var averageFactoryCondition = allFactories.Count > 0 ? allFactories.Average(factory => factory.Condition) : 1m;
            var factoriesUnderRepair = allFactories.Count(factory => factory.IsUnderRepair);
            var forcedRepairEvents = appended.Count(entry => entry.Change is FactoryEnteredRepair);

            turns.Add(new TurnMetrics
            {
                Turn = session.State.CurrentTurn,
                TotalCash = totalCash,
                VolumeSoldToSystem = volumeSold,
                AverageFactoryCondition = averageFactoryCondition,
                FactoriesUnderRepairCount = factoriesUnderRepair,
                ForcedRepairEventsCount = forcedRepairEvents,
                AverageConvergence = ComputeAverageConvergence(session, bots, materialCosts, idealHall, session.State.CurrentTurn),
            });
        });

        var finalScores = bots
            .Select(bot => FinalScoreCalculator.Calculate(
                session.State.Teams[bot.TeamId],
                materialCosts,
                session.State.Config.Raw.Economy,
                session.State.Config.Raw.FactoryDefinitions))
            .ToList();

        return new SessionMetrics
        {
            Turns = turns,
            FinalScores = finalScores,
            TeamCount = bots.Count,
            FinalConvergenceBySector = ComputeFinalConvergenceBySector(session, finalScores, idealHall),
        };
    }

    /// <summary>
    /// Прогоняет <paramref name="sessionCount"/> независимых партий — <paramref name="sessionFactory"/>
    /// собирает свежую сессию, ботов и генератор случайности для каждого прогона по его номеру
    /// (0-based), например, только меняя зерно жеребьёвки хода окончания — и сводит их метрики в
    /// один отчёт. <paramref name="idealHall"/> — см. doc-comment <see cref="RunSession"/>.
    /// </summary>
    public static BalancingReport RunMany(
        int sessionCount,
        Func<int, (GameSession Session, IReadOnlyList<SimpleBot> Bots, Random Random)> sessionFactory,
        IdealHallResult? idealHall = null)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        if (sessionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionCount), sessionCount, "Session count must be positive.");
        }

        var results = new List<SessionMetrics>();
        for (var i = 0; i < sessionCount; i++)
        {
            var (session, bots, random) = sessionFactory(i);
            results.Add(RunSession(session, bots, random, idealHall));
        }

        return BalancingReport.Summarize(results);
    }

    /// <summary>Score(t)/X(t) усреднённый по всем ботам партии на этот ход; <c>null</c>, если идеального зала нет или ни для одного бота нет данных (сектор отсутствует в X(t) или ход вышел за пределы просчитанного).</summary>
    private static decimal? ComputeAverageConvergence(
        GameSession session, IReadOnlyList<SimpleBot> bots, IReadOnlyDictionary<string, decimal> materialCosts,
        IdealHallResult? idealHall, int turn)
    {
        if (idealHall is null)
        {
            return null;
        }

        var ratios = new List<decimal>();
        foreach (var bot in bots)
        {
            var team = session.State.Teams[bot.TeamId];
            var idealValue = TryGetIdealValue(idealHall, team.Sector.Id, turn);
            // X(t) может быть отрицательным на раннем «дорогом старте» идеального зала (эталонная
            // политика вкладывает в потолок сразу у всех фабрик, окупается не сразу — Блок 7.3.4,
            // doc-comment IdealHallCalculator) — доля от отрицательного или нулевого потолка не имеет
            // содержательного смысла («X% от ещё не окупившегося старта»), пропускаем такие ходы.
            if (idealValue is not { } value || value <= 0m)
            {
                continue;
            }

            var score = FinalScoreCalculator.Calculate(
                team, materialCosts, session.State.Config.Raw.Economy, session.State.Config.Raw.FactoryDefinitions).Score;
            ratios.Add(score / value);
        }

        return ratios.Count > 0 ? ratios.Average() : null;
    }

    /// <summary>Score(T)/X(T) на конец партии, по сектору, усреднённая по командам того же сектора — переиспользует уже посчитанные <paramref name="finalScores"/>, не считает их заново.</summary>
    private static IReadOnlyDictionary<string, decimal> ComputeFinalConvergenceBySector(
        GameSession session, IReadOnlyList<FinalScoreResult> finalScores, IdealHallResult? idealHall)
    {
        if (idealHall is null)
        {
            return new Dictionary<string, decimal>();
        }

        var finalTurn = session.State.CurrentTurn;
        var bySector = new Dictionary<string, List<decimal>>();
        foreach (var score in finalScores)
        {
            var sectorId = session.State.Teams[score.TeamId].Sector.Id;
            var idealValue = TryGetIdealValue(idealHall, sectorId, finalTurn);
            // X(t) может быть отрицательным на раннем «дорогом старте» идеального зала (эталонная
            // политика вкладывает в потолок сразу у всех фабрик, окупается не сразу — Блок 7.3.4,
            // doc-comment IdealHallCalculator) — доля от отрицательного или нулевого потолка не имеет
            // содержательного смысла («X% от ещё не окупившегося старта»), пропускаем такие ходы.
            if (idealValue is not { } value || value <= 0m)
            {
                continue;
            }

            if (!bySector.TryGetValue(sectorId, out var ratios))
            {
                ratios = new List<decimal>();
                bySector[sectorId] = ratios;
            }

            ratios.Add(score.Score / value);
        }

        return bySector.ToDictionary(entry => entry.Key, entry => entry.Value.Average());
    }

    /// <summary>X(turn) сектора <paramref name="sectorId"/> из <paramref name="idealHall"/>; <c>null</c>, если сектора нет в X(t) или ход вне просчитанного диапазона (например, конфиг звал сессию длиннее MaxTurns пресета, на котором строился идеальный зал).</summary>
    private static decimal? TryGetIdealValue(IdealHallResult idealHall, string sectorId, int turn)
    {
        var branch = idealHall.Branches.FirstOrDefault(b => b.SectorId == sectorId);
        if (branch is null || turn < 1 || turn > branch.ValueByTurn.Count)
        {
            return null;
        }

        return branch.ValueByTurn[turn - 1];
    }
}
