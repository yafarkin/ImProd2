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

        /// <summary>
        /// = <see cref="TotalCost"/> / <see cref="Workers"/> — та же логика, что <see
        /// cref="NaiveProfitPerWorker"/> (честная единица сравнения между фабриками/уровнями с разным
        /// числом параллельных фабрик), но БЕЗ цены/margin — чисто по расходам, без допущений о
        /// продажной цене (запрос пользователя). Не путать с прибылью: это деньги, которые фабрика
        /// ТРАТИТ на одного рабочего за ход, не зарабатывает — растёт с уровнем почти механически
        /// (рецепты выше по цепочке комбинируют несколько уже дорогих входов в один результат), само по
        /// себе НЕ доказывает, что развивать уровень выгодно — для этого нужна выручка, см.
        /// <see cref="NaiveProfitPerWorker"/>.
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

        /// <summary>
        /// Наивная оценка выручки/прибыли — рынок специально исключён из себестоимости (см. doc-comment
        /// класса), но пользователь отдельно попросил проверить гипотезу «выгодно ли качаться до конца
        /// цепочки» (SPEC-история про подкову/иголку/пружину): <c>BasePrice материала × margin по
        /// уровню передела (<see cref="Game.Config.Economy.EconomyConfig.MarginMultiplierByProcessingLevel"/>)
        /// × выпуск</c> — та же формула, что <see cref="Game.Engine.MarketSaleCalculator"/> использует
        /// при продаже системе, но по БАЗОВОЙ цене/ёмкости, без текущей рыночной котировки, тренда и
        /// штрафа за превышение ёмкости рынка (`MarketCapacityOverflowDiscount`) — то есть верхняя
        /// граница, оптимистичная оценка, не прогноз реальной выручки. <c>null</c>, если для материала
        /// нет записи в <c>BaseMarketPerMaterial</c>.
        /// </summary>
        public decimal? NaiveBasePrice { get; init; }

        /// <summary>Множитель маржи по уровню передела этого материала (1 — уровень без записи в конфиге).</summary>
        public required decimal NaiveMarginMultiplier { get; init; }

        /// <summary>= <see cref="OutputQuantity"/> × <see cref="NaiveBasePrice"/> × <see cref="NaiveMarginMultiplier"/>, см. doc-comment <see cref="NaiveBasePrice"/>.</summary>
        public decimal? NaiveRevenue { get; init; }

        /// <summary>= <see cref="NaiveRevenue"/> − <see cref="TotalCost"/>.</summary>
        public decimal? NaiveProfit { get; init; }

        /// <summary>
        /// = <see cref="NaiveProfit"/> / <see cref="Workers"/> — честная мера для сравнения фабрик и
        /// уровней между собой (запрос пользователя): «Итого на уровень»/«Итого по фабрике» нельзя
        /// сравнивать напрямую между уровнями/секторами с разным числом параллельных фабрик — рабочие
        /// же на каждой фабрике одинаковы (общий параметр всего расчёта), поэтому именно они —
        /// сопоставимая единица вложения.
        /// </summary>
        public decimal? NaiveProfitPerWorker { get; init; }
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
        var electricityRate = config.Raw.Economy.ElectricityConsumptionPerOutputUnit;
        var electricityPrice = config.Raw.Economy.ElectricityBasePrice;
        var productivity = config.Raw.WorkerProductivity;
        var rnd = config.Raw.Rnd;
        var basePriceByMaterialId = config.Raw.Economy.BaseMarketPerMaterial.ToDictionary(m => m.MaterialId, m => m.BasePrice);
        var marginByLevel = config.Raw.Economy.MarginMultiplierByProcessingLevel.ToDictionary(m => m.Level, m => m.MarginMultiplier);

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

            var naiveMargin = marginByLevel.GetValueOrDefault(recipe.Output.Level, 1m);
            var naiveBasePrice = basePriceByMaterialId.TryGetValue(materialId, out var basePrice) ? basePrice : (decimal?)null;
            var naiveRevenue = naiveBasePrice.HasValue ? outputQuantity * naiveBasePrice.Value * naiveMargin : (decimal?)null;
            var naiveProfit = naiveRevenue.HasValue ? naiveRevenue.Value - totalCost : (decimal?)null;
            var naiveProfitPerWorker = naiveProfit.HasValue ? naiveProfit.Value / workersPerFactory : (decimal?)null;

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
                CostPerWorker = totalCost / workersPerFactory,
                RawMaterialsPerUnit = rawMaterialsPerUnit,
                NaiveBasePrice = naiveBasePrice,
                NaiveMarginMultiplier = naiveMargin,
                NaiveRevenue = naiveRevenue,
                NaiveProfit = naiveProfit,
                NaiveProfitPerWorker = naiveProfitPerWorker,
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
