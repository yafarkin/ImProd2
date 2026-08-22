using Game.Bots;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Balancing;

/// <summary>
/// Оркестровка <c>--mode calibrate</c> (Блок «автоподбор параметров», rebalance/2-sector-stepwise,
/// 2026-08-22) — печатает ход бисекции (<see cref="Calibrator"/>) построчно в консоль и итог: какое
/// значение рычага (<see cref="CalibrationLever"/>) даёт метрике (X(T) идеального зала или Score(T)
/// реального бота) заданную цель. Найденное значение НЕ применяется к файлам конфига автоматически —
/// печатается как рекомендация, применить руками (тем же приёмом, что и весь этот rebalance) —
/// осознанное решение: калибратор один прогон видит, живой человек видит куда больше контекста
/// (docs/TODO.md №27, монотонность по другим осям, и т.д.), не должен быть слепым к нему.
/// </summary>
internal static class CalibrateRun
{
    public static Task RunAsync(ResolvedGameConfig config, CliArguments cliArguments)
    {
        if (cliArguments.CalibrateLever is not { } leverName)
        {
            throw new ArgumentException("'--mode calibrate' требует '--calibrate-lever <имя>'.");
        }
        if (!CalibrationLever.All.TryGetValue(leverName, out var lever))
        {
            var known = string.Join(", ", CalibrationLever.All.Keys.OrderBy(k => k, StringComparer.Ordinal));
            throw new ArgumentException($"Неизвестный '--calibrate-lever' '{leverName}'. Известные: {known}.");
        }
        if (cliArguments.CalibrateMin is not { } min || cliArguments.CalibrateMax is not { } max)
        {
            throw new ArgumentException("'--mode calibrate' требует '--calibrate-min' и '--calibrate-max'.");
        }

        var preset = config.Raw.SessionPresets.Single(p => p.Id == cliArguments.PresetId);
        var target = cliArguments.CalibrateTarget;
        var tolerance = cliArguments.CalibrateTolerance;
        var maxIterations = cliArguments.CalibrateMaxIterations;

        Console.WriteLine(
            $"Калибрую рычаг '{leverName}' ({lever.Description}) на отрезке [{min}, {max}], " +
            $"метрика — {(cliArguments.CalibrateMetric == CalibrateMetric.Score ? $"Score({preset.MaxTurns})" : $"X({preset.MaxTurns})")}, " +
            $"цель {target:F0} ± {tolerance:F0}, не больше {maxIterations} итераций сверх двух граничных.");
        Console.WriteLine();

        Func<ResolvedGameConfig, decimal> evaluateMetric = cliArguments.CalibrateMetric == CalibrateMetric.Score
            ? resolved => EvaluateScore(resolved, preset, cliArguments)
            : resolved => IdealHallCalculator.Calculate(resolved, preset.MaxTurns).Branches.Sum(b => b.ValueByTurn[^1]);

        var result = Calibrator.FindTarget(
            config.Raw, lever.Apply, evaluateMetric, target, min, max, tolerance, maxIterations,
            trace: Console.WriteLine);

        Console.WriteLine();
        if (!result.Bracketed)
        {
            Console.WriteLine(
                $"НЕ УДАЛОСЬ взять цель в вилку на отрезке [{min}, {max}] — ближайшее из проверенного: " +
                $"параметр={result.BestParamValue:F4}, метрика={result.BestMetricValue:F0} (цель {target:F0}).");
            return Task.CompletedTask;
        }

        Console.WriteLine(
            $"Найдено за {result.Iterations.Count} вычислений метрики: параметр '{leverName}' = " +
            $"{result.BestParamValue:F4}, метрика = {result.BestMetricValue:F0} (цель {target:F0} ± {tolerance:F0}).");
        Console.WriteLine("Значение не применено к файлам конфига автоматически — примените руками, если устраивает.");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Score(T) реального бота (сумма по всем командам) — та же детерминированная сборка сессии и тот
    /// же цикл ходов, что <see cref="BotSessionRunner.RunToCompletion"/> использует харнесс балансировки
    /// (Блок 7.2), без построчной трассировки (<see cref="TraceRun"/>) — здесь важна только итоговая
    /// метрика, не разбор по ходам, поэтому быстрее гонять десятки раз подряд внутри бисекции.
    /// </summary>
    private static decimal EvaluateScore(ResolvedGameConfig config, Config.Session.SessionPresetConfig preset, CliArguments cliArguments)
    {
        var teams = new List<TeamSpec>();
        var bots = new List<SimpleBot>();
        foreach (var sector in config.Sectors)
        {
            for (var t = 0; t < cliArguments.TeamsPerSector; t++)
            {
                var teamId = Ulid.NewUlid();
                teams.Add(new TeamSpec { Id = teamId, Name = $"{sector.Id}-{t}", SectorId = sector.Id });
                bots.Add(new SimpleBot(teamId, sector, config, cliArguments.MaintainFactories, cliArguments.Leverage, cliArguments.Profile));
            }
        }

        // Тот же горизонт, что и X(T) (детерминированный EndTurn=MaxTurns, не случайная жеребьёвка) —
        // см. TraceRun.cs, step12: иначе метрика сравнивала бы разные T на каждой итерации бисекции.
        var session = GameSession.StartWithEndTurn(config, preset.Id, preset.MaxTurns, teams);
        BotSessionRunner.RunToCompletion(session, bots, new Random(2));

        var materialCosts = MaterialCostCalculator.CalculateAll(config);
        return session.State.Teams.Values.Sum(team =>
            FinalScoreCalculator.Calculate(team, materialCosts, config.Raw.FactoryDefinitions).Score);
    }
}

/// <summary>Какую метрику подгоняет <c>--mode calibrate</c> к цели — см. <see cref="CalibrateRun"/>.</summary>
internal enum CalibrateMetric
{
    /// <summary>X(T) идеального зала — быстрее (без прогона бота), детерминированная закрытая формула.</summary>
    X,

    /// <summary>Score(T) реального <see cref="SimpleBot"/> — то, что действительно интересует, но медленнее считать.</summary>
    Score,
}
