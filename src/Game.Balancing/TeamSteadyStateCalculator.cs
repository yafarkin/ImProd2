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

        /// <summary>
        /// Сумма прибыли всех фабрик сектора при продаже 100% выпуска системе (та же наценка и та же
        /// база — собственный передел, — что и в
        /// <see cref="ProductionCostLevelCalculator.FactoryRecipeCost.ProfitPerTurn"/>). Это ЧИСТАЯ
        /// маржа сверх всех операционных расходов, включая зарплату: она уже внутри передела, поэтому
        /// <see cref="NetPerTurn"/> вычитает её не второй раз, а только вложения в поколение/R&amp;D.
        /// </summary>
        public required decimal ProfitPerTurn { get; init; }

        /// <summary>Суммарная зарплата всех рабочих сектора (все фабрики уже построены и полностью укомплектованы тем же числом рабочих, что в <see cref="ProductionCostLevelCalculator.Calculate"/>) — справочно: в <see cref="NetPerTurn"/> отдельным слагаемым не входит, см. <see cref="ProfitPerTurn"/>.</summary>
        public required decimal SalaryPerTurn { get; init; }

        /// <summary>Потолок вложений в командное исследование поколений — один на сектор/команду, не на фабрику.</summary>
        public required decimal GenerationResearchPerTurn { get; init; }

        /// <summary>Потолок вложений в R&amp;D — по потолку на КАЖДУЮ фабрику сектора (в отличие от поколения, R&amp;D назначается отдельно каждой фабрике).</summary>
        public required decimal RndPerTurn { get; init; }

        /// <summary>Сколько фабрик в секторе — нужно, чтобы пересчитать потолок R&amp;D «на фабрику» из суммарного (<see cref="RndPerTurn"/>).</summary>
        public required int FactoryCount { get; init; }

        /// <summary>Потолок R&amp;D на одну фабрику из конфига — чтобы рецепт печатал «с X до ≤Y», а не только целевое число.</summary>
        public required decimal RndCeilingPerFactory { get; init; }

        /// <summary>Чистый поток за ход в устойчивом состоянии — <c>&lt; 0</c> значит цепочка не может свести концы с концами, даже когда всё уже построено и капитальные расходы позади. Зарплата вычитается не здесь, а внутри <see cref="ProfitPerTurn"/> (она часть передела, см. его doc-comment).</summary>
        public decimal NetPerTurn => ProfitPerTurn - GenerationResearchPerTurn - RndPerTurn;

        /// <summary>
        /// Готовый рецепт правки для секции §1c диагностики — не «где-то не сходится», а три
        /// конкретных числа, каждое из которых закрывает разрыв в одиночку (запрос пользователя
        /// 2026-09-07: инструмент должен показывать, ЧТО править, а не только ЧТО сломано).
        ///
        /// <para>
        /// Все три рычага независимы, поэтому и предлагаются как альтернативы, а не как сумма. Тот,
        /// который окажется отрицательным, физически недостижим (например, разрыв больше, чем ВСЕ
        /// вложения в R&amp;D вместе взятые) — такой вариант в рецепт не попадает вовсе, чтобы не
        /// советовать невозможное.
        /// </para>
        /// </summary>
        public string FormatPrescription()
        {
            var gap = -NetPerTurn;
            var options = new List<string>();

            var newRndCeiling = FactoryCount > 0 ? (ProfitPerTurn - GenerationResearchPerTurn) / FactoryCount : 0m;
            if (newRndCeiling > 0m)
            {
                options.Add($"Rnd.MaxCommitmentPerTurn с {RndCeilingPerFactory:F0} до ≤{newRndCeiling:F0} на фабрику");
            }

            var newGenerationCeiling = ProfitPerTurn - RndPerTurn;
            if (newGenerationCeiling > 0m)
            {
                options.Add($"GenerationResearch.MaxCommitmentPerTurn с {GenerationResearchPerTurn:F0} до ≤{newGenerationCeiling:F0}");
            }

            if (ProfitPerTurn > 0m)
            {
                options.Add($"суммарный передел сектора +{gap / ProfitPerTurn:P0} (глубже или шире цепочка)");
            }

            var head = $"{SectorId}: не хватает {gap:F0} ¤/ход в устойчивом состоянии.";
            return options.Count == 0
                ? head + " Ни один из рычагов (потолки вложений, глубина цепочки) разрыв не закрывает — сектор нежизнеспособен как есть."
                : head + " Закрывается любым из: " + string.Join("; ", options) + ".";
        }
    }

    public static IReadOnlyList<SectorSteadyState> Calculate(
        IReadOnlyList<ProductionCostLevelCalculator.FactoryRecipeCost> rows, ResolvedGameConfig config)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);

        var salaryPerWorker = config.Raw.WorkerProductivity.SalaryPerWorkerPerTurn;
        var generationCeiling = config.Raw.GenerationResearch.MaxCommitmentPerTurn;
        var rndCeilingPerFactory = config.Raw.Rnd.MaxCommitmentPerTurn;

        return rows
            .GroupBy(r => r.SectorId)
            .Select(group =>
            {
                var rowList = group.ToList();
                return new SectorSteadyState
                {
                    SectorId = group.Key,
                    ProfitPerTurn = rowList.Sum(r => r.ProfitPerTurn),
                    SalaryPerTurn = rowList.Sum(r => r.Workers) * salaryPerWorker,
                    GenerationResearchPerTurn = generationCeiling,
                    RndPerTurn = rowList.Count * rndCeilingPerFactory,
                    FactoryCount = rowList.Count,
                    RndCeilingPerFactory = rndCeilingPerFactory,
                };
            })
            .OrderBy(s => s.SectorId, StringComparer.Ordinal)
            .ToList();
    }
}
