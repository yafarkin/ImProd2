using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Balancing;

/// <summary>
/// Себестоимость производства без учёта хода/рынка/рабочих-как-расхода (запрос пользователя — «давай
/// возвращаться к аналитическому расчёту»): для каждой пары (фабрика, рецепт) в конфиге — сколько ¤
/// стоит её выпуск за один ход при заданном фиксированном числе рабочих на КАЖДОЙ фабрике, если
/// считать это число рабочих одинаковым и для металлургии, и для нефтехимии, и для любой другой
/// отрасли (сравнимая «линейка» между отраслями). В расходы входят только <see
/// cref="Game.Config.Catalog.FactoryDefinitionConfig.FixedCostPerTurn"/> и электричество (по базовой,
/// не рыночной цене — рынок сознательно не учитывается) плюс себестоимость входного сырья, взятая
/// рекурсивно по той же формуле («материал другой отрасли включается по своей себестоимости, как
/// будто мы произвели его сами»). Сознательно исключены зарплата (по запросу пользователя — считаем
/// без неё), R&amp;D и капремонт — оба параметра единые на всю игру (не варьируются по сектору, см.
/// doc-comment <see cref="Game.Config.Economy.RndConfig"/>/<see cref="Game.Config.Economy.WearConfig"/>),
/// поэтому на межотраслевое сравнение не влияют, а свести их к «ставке за ход» без произвольных
/// допущений о цикле не получится. Выпуск считается по той же формуле, что и настоящий движок (<see
/// cref="ProductionCalculator.CalculateCapacityBreakdown"/>) — на свежепостроенной фабрике первого
/// уровня, без простоя, чтобы не дублировать формулу мощности рабочих (линейно до <see
/// cref="Game.Config.Economy.WorkerProductivityConfig.BaseWorkerCount"/>, дальше — убывающая отдача).
/// Сюда же (запрос пользователя, rebalance/2-sector-stepwise, 2026-08-23, направление A плана
/// исследований <c>docs/rebalance-2sector/balance-experiment-plan.md</c>) — окупаемость каждого
/// уровня в изоляции (<see cref="FactoryRecipeCost.PaybackTurns"/>), без хода/бота/кросс-торговли,
/// тот же дешёвый статический снимок, что и себестоимость.
/// </summary>
public static class ProductionCostLevelCalculator
{
    /// <summary>Одна строка входного сырья в разбивке расходов фабрики — материал, сколько взято за ход и по какой себестоимости.</summary>
    public sealed record InputLine(string MaterialId, decimal Quantity, decimal UnitCost, decimal LineCost);

    /// <summary>Себестоимость одной пары (фабрика, рецепт) при фиксированном числе рабочих — единица агрегации отчёта.</summary>
    public sealed record FactoryRecipeCost
    {
        public required string SectorId { get; init; }
        public required string SectorName { get; init; }
        public required int Level { get; init; }
        public required string FactoryId { get; init; }
        public required string FactoryName { get; init; }
        public required string RecipeId { get; init; }
        public required string OutputMaterialId { get; init; }
        public required int Workers { get; init; }
        public required decimal OutputQuantity { get; init; }
        public required IReadOnlyList<InputLine> Inputs { get; init; }
        public required decimal InputCost { get; init; }
        public required decimal FixedCostPerTurn { get; init; }
        public required decimal ElectricityCost { get; init; }
        public required decimal TotalCost { get; init; }
        public required decimal UnitCost { get; init; }

        /// <summary>Цена постройки этой фабрики — нужна для <see cref="PaybackTurns"/>, сама по себе в себестоимость выпуска не входит (однократный расход, не за ход).</summary>
        public required decimal BuildCost { get; init; }

        /// <summary>
        /// Собственный передел фабрики за ход — <c>FixedCostPerTurn + электричество + зарплата</c>,
        /// то есть всё, что фабрика добавляет к стоимости входов. Именно от него, а не от полной
        /// себестоимости, считается <see cref="ProfitPerTurn"/> — см. его doc-comment.
        /// </summary>
        public required decimal ConversionCost { get; init; }

        /// <summary>
        /// Прибыль за ход при том же консервативном допущении, что и <see cref="PaybackTurns"/> — 100%
        /// выпуска продаётся системе по фиксированной наценке, без кросс-торговли. НЕ зависит от <see
        /// cref="BuildCost"/> (тот однократный расход, здесь — только выпуск/себестоимость/наценка) —
        /// поэтому обратная задача (направление B плана исследований,
        /// <c>docs/rebalance-2sector/balance-experiment-plan.md</c>: «по целевому сроку окупаемости
        /// найти допустимый BuildCost») — это просто <c>ProfitPerTurn × целевой срок</c>, без бисекции,
        /// см. <see cref="MaxBuildCostForTargetPayback"/>.
        /// <para>
        /// Считается от <see cref="ConversionCost"/> (собственный передел), НЕ от <see cref="TotalCost"/>
        /// (2026-09-06, <c>docs/economy-accounting-audit.md</c>, дефект 3): входы фабрика либо покупает
        /// (дешевле <c>себестоимость × наценка</c> никто не продаст — это пол продавца, системная
        /// продажа), либо производит сама — и тогда маржа на них уже начислена уровню-производителю.
        /// Прежняя формула <c>TotalCost × (наценка − 1)</c> начисляла маржу на себестоимость входов по
        /// разу на КАЖДОМ уровне вертикальной цепочки: на боевом <c>metallurgy.json</c> сумма прибыли
        /// выходила 3 940 ¤/ход против истинного максимума притока 1 764 ¤/ход, завышение в 2.23×.
        /// Сумма <see cref="ProfitPerTurn"/> по конфигу обязана совпадать с
        /// <c>0.30 × Σ передела</c> — тождеством денежной массы при cost-plus.
        /// </para>
        /// </summary>
        public required decimal ProfitPerTurn { get; init; }

        /// <summary>
        /// Срок окупаемости (ходов) при самом консервативном допущении — 100% выпуска продаётся
        /// СИСТЕМЕ по фиксированной наценке (<see cref="MarketSaleCalculator.SystemSaleMarginMultiplier"/>),
        /// кросс-торговля не учитывается вовсе (запрос пользователя, rebalance/2-sector-stepwise,
        /// 2026-08-23 — «идти с конца цепочки»: направление A плана исследований,
        /// <c>docs/rebalance-2sector/balance-experiment-plan.md</c>). Не занижение специально —
        /// доказано ранее в этой же ветке, что в симметричной топологии P2P-обмен даёт команде чистый
        /// ноль (продано ровно столько же, сколько куплено), весь реальный доход всё равно идёт через
        /// системную продажу; окупаемость на этом полу — самый честный, не оптимистичный тест.
        /// <c>null</c> — уровень никогда не окупится (прибыль с продажи ≤ 0).
        /// </summary>
        public decimal? PaybackTurns { get; init; }

        /// <summary>
        /// Обратная задача направления B (<c>docs/rebalance-2sector/balance-experiment-plan.md</c>,
        /// 2026-08-24): наибольший <see cref="BuildCost"/>, при котором окупаемость ещё укладывается в
        /// <paramref name="targetPaybackTurns"/> ходов — <c>ProfitPerTurn × targetPaybackTurns</c>
        /// (прямая формула, не бисекция, потому что <see cref="ProfitPerTurn"/> от <see
        /// cref="BuildCost"/> не зависит). <c>0</c> — уровень никогда не окупится ни при каком
        /// положительном <see cref="BuildCost"/> (нулевая или отрицательная маржа с продажи).
        /// </summary>
        public decimal MaxBuildCostForTargetPayback(decimal targetPaybackTurns) =>
            ProfitPerTurn > 0m ? ProfitPerTurn * targetPaybackTurns : 0m;

        /// <summary>
        /// = <see cref="TotalCost"/> / <see cref="Workers"/> — честная единица сравнения между
        /// фабриками/уровнями с разным числом параллельных фабрик (запрос пользователя: «Итого на
        /// уровень»/«Итого по фабрике» напрямую сравнивать нельзя — искажает картина, если на одном
        /// уровне 3 фабрики, а на другом 1). Это расход (деньги, которые фабрика ТРАТИТ на одного
        /// рабочего за ход), не доход — сам факт роста этой величины с уровнем не доказывает, что
        /// развивать уровень выгодно (для этого нужна была бы цена продажи, а её здесь принципиально
        /// нет — см. doc-comment класса).
        /// </summary>
        public required decimal CostPerWorker { get; init; }

        /// <summary>
        /// Сколько сырья (материалов уровня 0) нужно рекурсивно на 1 единицу выпуска этого рецепта —
        /// развёрнуто до самых первых, добывающих фабрик, включая межотраслевые связи (запрос
        /// пользователя: «сколько в итоге надо породы для 1 автомобиля, рекурсивно»). Ключ — Id
        /// сырьевого материала, значение — количество на единицу; для самого сырья это тривиально
        /// {себя же: 1}.
        /// </summary>
        public required IReadOnlyDictionary<string, decimal> RawMaterialsPerUnit { get; init; }
    }

    /// <summary>
    /// Считает <see cref="FactoryRecipeCost"/> для каждой пары (фабрика, рецепт) конфига. Порядок
    /// вычисления — рекурсивный по пирамиде входов (тот же приём, что <see
    /// cref="Game.Domain.CostCalculator.CalculateUnitCost"/>, но себестоимость сырья не «заданная
    /// базовая цена», а посчитанные расходы конкретной добывающей фабрики), с мемоизацией по
    /// материалу-выходу — независимо от того, в каком порядке выписаны рецепты в JSON.
    /// </summary>
    public static IReadOnlyList<FactoryRecipeCost> Calculate(ResolvedGameConfig config, int workersPerFactory)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (workersPerFactory <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workersPerFactory), workersPerFactory, "Worker count must be positive.");
        }

        var fixedCostByFactoryId = config.Raw.FactoryDefinitions.ToDictionary(d => d.Id, d => d.FixedCostPerTurn);
        var buildCostByFactoryId = config.Raw.FactoryDefinitions.ToDictionary(d => d.Id, d => d.BuildCost);
        var electricityRate = config.Raw.Economy.ElectricityConsumptionPerOutputUnit;
        var electricityPrice = config.Raw.Economy.ElectricityBasePrice;
        var productivity = config.Raw.WorkerProductivity;
        var rnd = config.Raw.Rnd;
        // Зарплата НЕ входит в UnitCost (сознательно, см. doc-comment класса — та величина для
        // сравнения отраслей между собой), но входит в собственный передел, от которого считается
        // ProfitPerTurn: там речь про настоящие деньги (docs/economy-accounting-audit.md, шаг 3).
        var salaryCost = workersPerFactory * productivity.SalaryPerWorkerPerTurn;

        var producerByMaterialId = new Dictionary<string, (FactoryDefinition FactoryDef, Recipe Recipe)>();
        foreach (var factoryDef in config.FactoryDefinitions)
        {
            foreach (var recipe in factoryDef.Recipes)
            {
                if (!producerByMaterialId.TryAdd(recipe.Output.Id, (factoryDef, recipe)))
                {
                    var existing = producerByMaterialId[recipe.Output.Id];
                    throw new NotSupportedException(
                        $"Материал '{recipe.Output.Id}' производится больше чем одним рецептом " +
                        $"('{existing.Recipe.Id}' у '{existing.FactoryDef.Id}' и '{recipe.Id}' у '{factoryDef.Id}') — " +
                        "этот калькулятор пока не умеет выбирать между альтернативными рецептами одного материала " +
                        "(см. project_multi_recipe_factory_gap), нужен явный признак, какой рецепт считать основным.");
                }
            }
        }

        var rowByMaterialId = new Dictionary<string, FactoryRecipeCost>();
        var inProgress = new HashSet<string>();

        FactoryRecipeCost Resolve(string materialId)
        {
            if (rowByMaterialId.TryGetValue(materialId, out var cached))
            {
                return cached;
            }
            if (!inProgress.Add(materialId))
            {
                throw new NotSupportedException($"Циклическая зависимость рецептов через материал '{materialId}'.");
            }

            if (!producerByMaterialId.TryGetValue(materialId, out var producer))
            {
                throw new NotSupportedException(
                    $"Материал '{materialId}' используется как вход рецепта, но ни одна фабрика в конфиге его не производит.");
            }

            var (factoryDef, recipe) = producer;
            var factory = new Factory(Ulid.NewUlid(), factoryDef.Sector, factoryDef, recipe);
            factory.Hire(workersPerFactory);
            var breakdown = ProductionCalculator.CalculateCapacityBreakdown(factory, productivity, rnd);
            var outputQuantity = breakdown.TheoreticalMaxOutput;
            var batches = outputQuantity / recipe.OutputQuantity;

            var inputLines = new List<InputLine>();
            var rawMaterialsPerUnit = new Dictionary<string, decimal>();
            if (recipe.Inputs.Count == 0)
            {
                // Сырьё уровня 0 — рекурсия дна: на 1 единицу себя же нужна 1 единица себя же.
                rawMaterialsPerUnit[materialId] = 1m;
            }

            foreach (var input in recipe.Inputs)
            {
                var inputRow = Resolve(input.Material.Id);
                var quantity = input.Quantity * batches;
                inputLines.Add(new InputLine(input.Material.Id, quantity, inputRow.UnitCost, quantity * inputRow.UnitCost));

                var perUnitOfOutput = input.Quantity / recipe.OutputQuantity;
                foreach (var (rawMaterialId, rawQuantityPerUnitOfInput) in inputRow.RawMaterialsPerUnit)
                {
                    rawMaterialsPerUnit.TryGetValue(rawMaterialId, out var existing);
                    rawMaterialsPerUnit[rawMaterialId] = existing + perUnitOfOutput * rawQuantityPerUnitOfInput;
                }
            }

            var inputCost = inputLines.Sum(line => line.LineCost);
            var fixedCostPerTurn = fixedCostByFactoryId[factoryDef.Id];
            var electricityCost = outputQuantity * electricityRate * electricityPrice;
            var totalCost = inputCost + fixedCostPerTurn + electricityCost;
            var unitCost = outputQuantity > 0 ? totalCost / outputQuantity : 0m;
            var buildCost = buildCostByFactoryId[factoryDef.Id];
            var conversionCost = fixedCostPerTurn + electricityCost + salaryCost;
            var profitPerTurn = conversionCost * (MarketSaleCalculator.SystemSaleMarginMultiplier - 1m);
            var paybackTurns = profitPerTurn > 0m ? buildCost / profitPerTurn : (decimal?)null;

            var row = new FactoryRecipeCost
            {
                SectorId = factoryDef.Sector.Id,
                SectorName = factoryDef.Sector.Name,
                Level = recipe.Output.Level,
                FactoryId = factoryDef.Id,
                FactoryName = factoryDef.Name,
                RecipeId = recipe.Id,
                OutputMaterialId = materialId,
                Workers = workersPerFactory,
                OutputQuantity = outputQuantity,
                Inputs = inputLines,
                InputCost = inputCost,
                FixedCostPerTurn = fixedCostPerTurn,
                ElectricityCost = electricityCost,
                TotalCost = totalCost,
                UnitCost = unitCost,
                BuildCost = buildCost,
                ConversionCost = conversionCost,
                ProfitPerTurn = profitPerTurn,
                PaybackTurns = paybackTurns,
                CostPerWorker = totalCost / workersPerFactory,
                RawMaterialsPerUnit = rawMaterialsPerUnit,
            };

            inProgress.Remove(materialId);
            rowByMaterialId[materialId] = row;
            return row;
        }

        foreach (var materialId in producerByMaterialId.Keys)
        {
            Resolve(materialId);
        }

        return rowByMaterialId.Values
            .OrderBy(r => r.SectorId, StringComparer.Ordinal)
            .ThenBy(r => r.Level)
            .ThenBy(r => r.FactoryId, StringComparer.Ordinal)
            .ThenBy(r => r.RecipeId, StringComparer.Ordinal)
            .ToList();
    }
}
