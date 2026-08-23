using Game.Config.Loading;
using Game.Engine;

namespace Game.Balancing;

/// <summary>
/// Продолжение направления A плана исследований (<c>docs/rebalance-2sector/balance-experiment-plan.md</c>,
/// 2026-08-23) — найдено при полном прогоне уже починенной по <see cref="ProductionCostLevelCalculator.FactoryRecipeCost.PaybackTurns"/>
/// синтетической цепочки: все уровни окупают себя ПО ОТДЕЛЬНОСТИ, но идеальный зал (<c>--mode
/// ideal-hall</c>) всё равно уходит в глубокий минус — окупаемость уровня сознательно не считает
/// зарплату и вложения в поколение/R&amp;D (см. doc-comment <see cref="ProductionCostLevelCalculator"/>
/// — они не варьируются по сектору), а на реальной партии это оказались две САМЫЕ большие статьи
/// расхода. Эта проверка — тот же дух («самый пессимистичный, но гарантированный сценарий, без хода»),
/// но не по одной фабрике, а по всей КОМАНДЕ разом: если бы вся цепочка сектора была уже построена и
/// полностью укомплектована рабочими, хватает ли суммарной прибыли с продажи системе, чтобы покрыть
/// зарплату всех рабочих и вложения в поколение/R&amp;D по потолку (<c>MaxCommitmentPerTurn</c>) —
/// эталонный темп идеального зала, не то, что реально вложит бот. Не учитывает капитальные расходы
/// (BuildCost, зарплата ЕЩЁ не построенных уровней) — это отдельный, более ранний период партии,
/// пока цепочка ещё не достроена; здесь проверяется именно УСТОЙЧИВОЕ состояние после того, как всё
/// уже готово.
/// </summary>
public static class TeamSteadyStateCalculator
{
    public sealed record SectorSteadyState
    {
        public required string SectorId { get; init; }

        /// <summary>Сумма прибыли всех фабрик сектора при продаже 100% выпуска системе (та же наценка, что и в <see cref="ProductionCostLevelCalculator.FactoryRecipeCost.PaybackTurns"/>).</summary>
        public required decimal ProfitPerTurn { get; init; }

        /// <summary>Суммарная зарплата всех рабочих сектора (все фабрики уже построены и полностью укомплектованы тем же числом рабочих, что в <see cref="ProductionCostLevelCalculator.Calculate"/>).</summary>
        public required decimal SalaryPerTurn { get; init; }

        /// <summary>Потолок вложений в командное исследование поколений — один на сектор/команду, не на фабрику.</summary>
        public required decimal GenerationResearchPerTurn { get; init; }

        /// <summary>Потолок вложений в R&amp;D — по потолку на КАЖДУЮ фабрику сектора (в отличие от поколения, R&amp;D назначается отдельно каждой фабрике).</summary>
        public required decimal RndPerTurn { get; init; }

        /// <summary>Чистый поток за ход в устойчивом состоянии — <c>&lt; 0</c> значит цепочка не может свести концы с концами, даже когда всё уже построено и капитальные расходы позади.</summary>
        public decimal NetPerTurn => ProfitPerTurn - SalaryPerTurn - GenerationResearchPerTurn - RndPerTurn;
    }

    public static IReadOnlyList<SectorSteadyState> Calculate(
        IReadOnlyList<ProductionCostLevelCalculator.FactoryRecipeCost> rows, ResolvedGameConfig config)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);

        var salaryPerWorker = config.Raw.WorkerProductivity.SalaryPerWorkerPerTurn;
        var generationCeiling = config.Raw.GenerationResearch.MaxCommitmentPerTurn;
        var rndCeilingPerFactory = config.Raw.Rnd.MaxCommitmentPerTurn;
        var margin = MarketSaleCalculator.SystemSaleMarginMultiplier - 1m;

        return rows
            .GroupBy(r => r.SectorId)
            .Select(group =>
            {
                var rowList = group.ToList();
                return new SectorSteadyState
                {
                    SectorId = group.Key,
                    ProfitPerTurn = rowList.Sum(r => r.TotalCost * margin),
                    SalaryPerTurn = rowList.Sum(r => r.Workers) * salaryPerWorker,
                    GenerationResearchPerTurn = generationCeiling,
                    RndPerTurn = rowList.Count * rndCeilingPerFactory,
                };
            })
            .OrderBy(s => s.SectorId, StringComparer.Ordinal)
            .ToList();
    }
}
