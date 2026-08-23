using Game.Bots;
using Game.Domain;
using Game.Engine;

namespace Game.Balancing.Tests;

/// <summary>
/// Направление D плана исследований (<c>docs/rebalance-2sector/balance-experiment-plan.md</c>,
/// 2026-08-24): «достижим ли идеал вообще какой-либо стратегией, или он тоже отчасти нарисован
/// неправильно» — постоянная проверка того, что <see cref="IdealHallCalculator"/> действительно
/// верхняя граница, не просто удобное число для сравнения. Прогоняет реального <see cref="SimpleBot"/>
/// по всей сетке <c>leverage</c>×<c>profile</c> (0..1 по обеим осям — весь диапазон, который бот вообще
/// умеет выражать, см. doc-comment <see cref="SimpleBot"/>) и требует, чтобы НИ ОДНА ячейка не дала
/// сходимость (<c>Score(T)/X(T)</c>) больше 100%.
/// <para>
/// <b>Почему это ожидаемо, не просто эмпирическая надежда</b> (аналитическое обоснование,
/// подтверждающее находку направления C о «мёртвых осях» под cost-plus наценкой): в текущей механике
/// нет ни займа, ни процента на отрицательный баланс (docs/TODO.md #23 — займ убран как класс),
/// поэтому у «вложить 100% потолка каждый ход» и «построить фабрику в тот же ход, когда она
/// разблокирована» — ровно то, что и делает <see cref="IdealHallCalculator"/> (см. его doc-comment,
/// «Допущения v1») — нет альтернативной издержки: тратить раньше физически не может быть хуже, чем
/// тратить позже. Значит эталонная стратегия зала не эвристика, которую в принципе можно обыграть, а
/// буквально физический максимум того, что можно сделать за ход при данных лимитах конфига — реальный
/// бот с любым <c>leverage</c>/<c>profile</c> по конструкции не может её обогнать.
/// </para>
/// </summary>
public class IdealHallUpperBoundTests
{
    [Fact]
    public void No_Leverage_Profile_Combination_Beats_The_Ideal_Hall()
    {
        // Числа реального фикса направления B (debug-minimal.json, эта же ветка) — не произвольные,
        // заведомо здоровая (окупающаяся) цепочка, чтобы сходимость была содержательной величиной, а
        // не постоянным null из-за X(T)<=0 (см. doc-comment BalancingHarness.ComputeAverageConvergence).
        var levels = new[]
        {
            new SyntheticChainConfigBuilder.LevelParams(BuildCost: 350m, FixedCostPerTurn: 6.67m, ProductionRate: 50m),
            new SyntheticChainConfigBuilder.LevelParams(BuildCost: 800m, FixedCostPerTurn: 20m, ProductionRate: 20m),
            new SyntheticChainConfigBuilder.LevelParams(BuildCost: 1650m, FixedCostPerTurn: 46.67m, ProductionRate: 7.5m),
        };
        var config = SyntheticChainConfigBuilder.Build(levels, inputQuantityPerLevel: 2m);
        var sector = config.Sectors.Single();
        var idealHall = IdealHallCalculator.Calculate(config, maxTurns: 90);

        var leverageLevels = StrategyGridRunner.UniformLevels(3);
        var profileLevels = StrategyGridRunner.UniformLevels(3);

        var results = StrategyGridRunner.Run(leverageLevels, profileLevels, sessionsPerCell: 3, (leverage, profile, sessionIndex) =>
        {
            var teamId = Ulid.NewUlid();
            var teams = new List<TeamSpec> { new() { Id = teamId, Name = "Бот", SectorId = sector.Id } };
            var bots = new List<SimpleBot> { new(teamId, sector, config, leverage: leverage, profile: profile) };
            var session = GameSession.StartWithEndTurn(config, "short", endTurn: 90, teams);
            return (session, (IReadOnlyList<SimpleBot>)bots, new Random(sessionIndex + 1));
        }, idealHall: idealHall);

        Assert.Contains(results, cell => cell.Report.OverallAverageFinalConvergence.HasValue);
        foreach (var cell in results)
        {
            if (cell.Report.OverallAverageFinalConvergence is not { } convergence)
            {
                continue;
            }

            Assert.True(
                convergence <= 1.001m,
                $"leverage={cell.Leverage}, profile={cell.Profile}: сходимость {convergence:P1} превышает 100% — " +
                "Score(T) обогнал X(T), идеальный зал больше не верхняя граница.");
        }
    }
}
