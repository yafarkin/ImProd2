using Game.Config.Economy;
using Game.Config.Loading;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Идеальный зал (Блок 7.3.4, <c>docs/production-balance.md</c> §4) — детерминированный,
/// воспроизводимый расчёт теоретического потолка X(t) по каждой ветке специализации конфига: не бот
/// и не Monte-Carlo симуляция, а одна совместная идеализированная система, где все ветки одновременно
/// идут по эталонной безошибочной стратегии, а взаимные потоки материалов между ними физически
/// ограничены реальной мощностью поставляющей ветки на тот же ход (§4, «важная поправка» — нельзя
/// считать ветку в изоляции с бесконечным предложением соседей).
///
/// <para><b>Допущения v1</b> (намеренные упрощения, см. §4 «Допущения v1», не итоговый дизайн):</para>
/// <list type="bullet">
/// <item>Обмен между ветками — по себестоимости (<see cref="CostCalculator"/>), без переговорной
/// надбавки; платёж за перевод идёт в обе стороны (продавец получает деньги, покупатель платит) —
/// перевод не бесплатный подарок, просто без монопольной наценки.</item>
/// <item>Остаток излишка материала, который не забрала ни одна соседняя ветка (после <see
/// cref="TransferAcrossBranches"/>), продаётся системе тем же ходом по <see
/// cref="MarketSaleCalculator"/> (котировка × <see cref="EconomyConfig.MarginMultiplierByProcessingLevel"/>
/// текущего уровня передела, включая понижающий коэффициент за превышение ёмкости) — аналог
/// <c>SimpleBot.SellSurplusToSystem</c> у реального бота, а не только пассивная оценка склада в конце
/// хода (см. <see cref="ComputeValue"/>). Добавлено намеренно: без этого X(t) сильно
/// недооценивал ветки с большим числом параллельных нисходящих переделов на одном сырье — у них
/// заметная доля выпуска не находит покупателя среди соседних веток и должна уходить в реальный
/// рыночный доход, а не лежать на складе по неполной цене (см. <c>docs/TODO.md</c> №2, находка сессии
/// 2026-08-15).</item>
/// <item>Полная информация, ноль ошибок: капремонт не нужен вовсе — состояние фабрики держится на 1.0
/// (не моделируем износ), эквивалент «капремонт всегда точно вовремя».</item>
/// <item>Темп вложений — эталонная постоянная доля потолка за ход, и для R&amp;D фабрики, и для
/// командного исследования поколений: 100% <see cref="RndConfig.MaxCommitmentPerTurn"/>/<see
/// cref="GenerationResearchConfig.MaxCommitmentPerTurn"/> каждый ход, пока не достигнут потолок
/// уровня/поколения (после — не списывается, как и в реальном движке, <see cref="RndInvestmentStep"/>/
/// <see cref="GenerationResearchStep"/>). Самое грубое упрощение v1 — настоящая оптимизация
/// (динамическая, не постоянная доля) — возможный апгрейд v2, не в этом классе.</item>
/// <item>Ограничение мощности: ветка не может получить от соседней больше, чем та произвела сверх
/// собственных нужд на этот же ход — естественное следствие того, что перевод (см. <see
/// cref="TransferAcrossBranches"/>) считается уже ПОСЛЕ производства этого хода, от фактического
/// остатка на складе, не от теоретического желания.</item>
/// </list>
///
/// <para>
/// Тот же простой P&amp;L, что и у <see cref="FinalScoreCalculator"/> (Cash + WarehouseValue +
/// FactoriesValue) — банковского займа как класса механики в игре больше нет (docs/TODO.md #23):
/// денежный остаток может свободно уходить в минус (аванс за раннюю постройку до первой выручки) —
/// это не ошибка, а просто отрицательное слагаемое суммы; кредитное плечо (<c>leverage</c>, Блок
/// 7.3.2) — отдельная ось калибровки ботов, не часть игрового эталона.
/// </para>
/// </summary>
public static class IdealHallCalculator
{
    /// <summary>
    /// Считает X(t) для каждого сектора <paramref name="config"/> на <paramref name="maxTurns"/>
    /// ходов. Чистая функция (без рандома) — два вызова с одним и тем же конфигом дают одно и то же.
    /// </summary>
    public static IdealHallResult Calculate(ResolvedGameConfig config, int maxTurns)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (maxTurns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTurns), maxTurns, "Turn count must be positive.");
        }

        var rawMaterialCosts = BuildRawMaterialCosts(config);
        var basePriceByMaterialId = config.Raw.Economy.BaseMarketPerMaterial
            .ToDictionary(m => m.MaterialId, m => m.BasePrice);
        var branches = config.Sectors.Select(sector => CreateBranch(config, sector)).ToList();
        var market = new Market();

        for (var turn = 1; turn <= maxTurns; turn++)
        {
            var marketUpdate = MarketCalculator.Calculate(turn, config.Raw.Economy);
            market.ReplaceQuotes(marketUpdate.Quotes, marketUpdate.ElectricityPrice);

            foreach (var branch in branches)
            {
                AdvanceGeneration(branch, config, turn);
                ChargeGenerationResearch(branch, config);
                BuildNewlyUnlockedFactories(branch, config, turn);
                AdvanceFactoryLevelsAndChargeRnd(branch, config, turn);
                RunProduction(branch, config);
                ChargeOperatingCosts(branch, config);
            }

            TransferAcrossBranches(branches, config, rawMaterialCosts, market);

            foreach (var branch in branches)
            {
                branch.ValueByTurn.Add(ComputeValue(branch, config, basePriceByMaterialId));
            }
        }

        return new IdealHallResult
        {
            Branches = branches.Select(b => new IdealHallBranchTrajectory
            {
                SectorId = b.Sector.Id,
                SectorName = b.Sector.Name,
                ValueByTurn = b.ValueByTurn,
            }).ToList(),
        };
    }

    /// <summary>Изменяемое состояние одной ветки в течение прогона — не публичный тип, живёт только внутри расчёта.</summary>
    private sealed class BranchState
    {
        public required Sector Sector { get; init; }
        public required Team Team { get; init; }
        public required IReadOnlyList<FactoryDefinition> SectorFactories { get; init; }
        public decimal Cash { get; set; }
        public int PreviousGeneration { get; set; }
        public Dictionary<Ulid, int> BuiltAtTurn { get; } = new();
        public Dictionary<Ulid, int> PreviousLevel { get; } = new();
        public List<decimal> ValueByTurn { get; } = new();
    }

    private static BranchState CreateBranch(ResolvedGameConfig config, Sector sector)
    {
        var startingGeneration = config.Raw.GenerationResearch.StartingGeneration;
        var team = new Team(Ulid.NewUlid(), $"{sector.Id} (идеальный зал)", sector, startingGeneration);
        var sectorFactories = config.FactoryDefinitions
            .Where(f => f.Sector == sector)
            .OrderBy(f => f.Recipes[0].Output.Level)
            .ToList();

        return new BranchState
        {
            Sector = sector,
            Team = team,
            SectorFactories = sectorFactories,
            PreviousGeneration = startingGeneration,
        };
    }

    /// <summary>Закрытая форма: сколько поколений разблокировало бы накопленное вложение «по потолку каждый ход» к этому ходу.</summary>
    private static void AdvanceGeneration(BranchState branch, ResolvedGameConfig config, int turn)
    {
        var genConfig = config.Raw.GenerationResearch;
        var cumulativeInvestment = turn * genConfig.MaxCommitmentPerTurn;
        var targetGeneration = GenerationResearchCalculator.CalculateResultingGeneration(
            genConfig.StartingGeneration, cumulativeInvestment, genConfig);

        while (branch.Team.UnlockedGeneration < targetGeneration)
        {
            branch.Team.AdvanceGeneration();
        }
    }

    /// <summary>Списывает командное вложение в исследование поколений за этот ход — по состоянию НА НАЧАЛО хода (см. doc-comment класса про остановку на потолке), затем фиксирует новое «начало хода» на следующий ход.</summary>
    private static void ChargeGenerationResearch(BranchState branch, ResolvedGameConfig config)
    {
        var genConfig = config.Raw.GenerationResearch;
        if (!GenerationResearchCalculator.IsAtMaxGeneration(branch.PreviousGeneration, genConfig))
        {
            branch.Cash -= genConfig.MaxCommitmentPerTurn;
        }

        branch.PreviousGeneration = branch.Team.UnlockedGeneration;
    }

    /// <summary>
    /// Единица достройки — пара (тип, рецепт), не сам тип (тот же принцип и то же обоснование, что и
    /// у <see cref="SimpleBot.BuildNewlyUnlockedFactories"/>, тем же именем не просто совпадение —
    /// оба должны сходиться в одном и том же выборе рецепта, иначе «идеальный зал» перестаёт быть
    /// честной верхней границей для реального бота, запрос пользователя, TODO.md #20, 2026-08-17):
    /// тип с несколькими рецептами даёт отдельную фабрику на каждый рецепт.
    /// </summary>
    private static void BuildNewlyUnlockedFactories(BranchState branch, ResolvedGameConfig config, int turn)
    {
        var builtCombinations = branch.Team.Factories.Select(f => (f.Definition.Id, f.SelectedRecipe.Id)).ToHashSet();
        var baseWorkerCount = config.Raw.WorkerProductivity.BaseWorkerCount;
        foreach (var definition in branch.SectorFactories)
        {
            foreach (var recipe in definition.Recipes)
            {
                if (builtCombinations.Contains((definition.Id, recipe.Id)) || recipe.Output.Level > branch.Team.UnlockedGeneration)
                {
                    continue;
                }

                var buildCost = config.Raw.FactoryDefinitions.First(d => d.Id == definition.Id).BuildCost;
                var factory = branch.Team.BuildFactory(Ulid.NewUlid(), definition, recipe, builtAtTurn: turn);
                factory.Hire(baseWorkerCount);
                branch.Cash -= buildCost;
                branch.BuiltAtTurn[factory.Id] = turn;
                branch.PreviousLevel[factory.Id] = 1;
            }
        }
    }

    /// <summary>Та же закрытая форма, что <see cref="AdvanceGeneration"/>, но на уровне одной фабрики — с момента её постройки.</summary>
    private static void AdvanceFactoryLevelsAndChargeRnd(BranchState branch, ResolvedGameConfig config, int turn)
    {
        var rndConfig = config.Raw.Rnd;
        foreach (var factory in branch.Team.Factories)
        {
            var previousLevel = branch.PreviousLevel[factory.Id];
            if (!RndCalculator.IsAtMaxLevel(previousLevel, rndConfig))
            {
                branch.Cash -= rndConfig.MaxCommitmentPerTurn;
            }

            var turnsSinceBuilt = turn - branch.BuiltAtTurn[factory.Id] + 1;
            var cumulativeInvestment = turnsSinceBuilt * rndConfig.MaxCommitmentPerTurn;
            var targetLevel = RndCalculator.CalculateResultingLevel(1, cumulativeInvestment, rndConfig);
            while (factory.Level < targetLevel)
            {
                factory.AdvanceLevel();
            }

            branch.PreviousLevel[factory.Id] = factory.Level;
        }
    }

    /// <summary>Тот же приём бottom-up по уровням, что и <see cref="GameSession.RunTick"/> — выше видит выход ниже за этот же ход. Себестоимость поступления в склад не считаем (см. doc-comment класса — X(t) оценивает склад по рыночной цене, не по cost basis), поэтому <c>cost: 0m</c>.</summary>
    private static void RunProduction(BranchState branch, ResolvedGameConfig config)
    {
        foreach (var levelGroup in branch.Team.Factories.GroupBy(f => f.SelectedRecipe.Output.Level).OrderBy(g => g.Key))
        {
            var factoriesAtLevel = levelGroup.OrderBy(f => f.Id).ToList();
            var results = ProductionCalculator.CalculateGroup(
                factoriesAtLevel, branch.Team.Warehouse, config.Raw.WorkerProductivity, config.Raw.Rnd);

            foreach (var result in results)
            {
                var factory = factoriesAtLevel.Single(f => f.Id == result.FactoryId);
                foreach (var (materialId, quantity) in result.ConsumedInputs)
                {
                    if (quantity <= 0)
                    {
                        continue;
                    }

                    var material = factory.SelectedRecipe.Inputs.First(input => input.Material.Id == materialId).Material;
                    branch.Team.Warehouse.Remove(material, quantity);
                }

                if (result.OutputQuantity > 0)
                {
                    branch.Team.Warehouse.Add(factory.SelectedRecipe.Output, result.OutputQuantity, cost: 0m);
                }
            }
        }
    }

    /// <summary>Зарплата и содержание фабрик — те же формулы, что реальный тик (<see cref="FinanceCalculator"/>); состояние всех фабрик — 1.0 (см. doc-comment класса), поэтому штрафа за износ в содержании нет.</summary>
    private static void ChargeOperatingCosts(BranchState branch, ResolvedGameConfig config)
    {
        var totalWorkers = branch.Team.Factories.Sum(f => f.Workers);
        branch.Cash -= FinanceCalculator.CalculateSalaries(totalWorkers, config.Raw.WorkerProductivity);
        branch.Cash -= FinanceCalculator.CalculateFactoryUpkeep(branch.Team.Factories, config.Raw.FactoryDefinitions, config.Raw.Wear);
    }

    /// <summary>
    /// Переводит материалы между ветками ПОСЛЕ того, как все они уже произвели этот ход (см.
    /// doc-comment класса — физическое ограничение мощности поставщика). У каждого материала не
    /// больше одной ветки-производителя (<see cref="Material.Sector"/>) — делить излишек между
    /// несколькими продавцами не от чего, только между покупателями: если суммарный дефицит
    /// покупателей больше излишка продавца, все получают одну и ту же долю своего дефицита
    /// («жадное», но справедливое распределение — не первый пришедший забирает всё). То, что после
    /// этого остаётся невостребованным соседями (частично или целиком — в т.ч. материалы, у которых
    /// вообще нет ветки-покупателя, например финальный флагман), продаётся системе тем же ходом (см.
    /// doc-comment класса, «намеренно добавлено») — не откладывается до пассивной оценки склада.
    /// </summary>
    private static void TransferAcrossBranches(
        IReadOnlyList<BranchState> branches, ResolvedGameConfig config, IReadOnlyDictionary<Material, decimal> rawMaterialCosts,
        Market market)
    {
        foreach (var material in config.Materials.Values)
        {
            var seller = branches.FirstOrDefault(b => b.Sector == material.Sector);
            if (seller is null)
            {
                continue;
            }

            var surplus = ComputeSurplus(seller, material, config);
            if (surplus <= 0m)
            {
                continue;
            }

            var remainingSurplus = surplus - TransferToBuyers(seller, branches, material, surplus, config, rawMaterialCosts);
            SellRemainingSurplusToSystem(seller, material, remainingSurplus, config, market);
        }
    }

    /// <summary>Раздаёт излишек продавца соседним веткам-покупателям по себестоимости (см. doc-comment <see cref="TransferAcrossBranches"/>). Возвращает фактически переданное количество — остаток после этого не покупателям, а системе (<see cref="SellRemainingSurplusToSystem"/>).</summary>
    private static decimal TransferToBuyers(
        BranchState seller, IReadOnlyList<BranchState> branches, Material material, decimal surplus,
        ResolvedGameConfig config, IReadOnlyDictionary<Material, decimal> rawMaterialCosts)
    {
        var buyers = branches
            .Where(b => b != seller)
            .Select(b => (Branch: b, Deficit: ComputeDeficit(b, material, config)))
            .Where(entry => entry.Deficit > 0m)
            .ToList();
        if (buyers.Count == 0)
        {
            return 0m;
        }

        var totalDeficit = buyers.Sum(entry => entry.Deficit);
        var fillRatio = Math.Min(1m, surplus / totalDeficit);
        if (fillRatio <= 0m || !TryCalculateUnitCost(material, config.RecipeBook, rawMaterialCosts, out var unitCost))
        {
            return 0m;
        }

        var transferredTotal = 0m;
        foreach (var (buyer, deficit) in buyers)
        {
            var quantity = deficit * fillRatio;
            if (quantity <= 0m)
            {
                continue;
            }

            seller.Team.Warehouse.Remove(material, quantity);
            buyer.Team.Warehouse.Add(material, quantity, cost: 0m);

            // По себестоимости (doc-comment класса) — не бесплатный подарок и не переговорная
            // наценка: продавец не беднеет от передачи (склад минус, касса плюс на ту же сумму),
            // покупатель платит ровно то, во что материал обошёлся бы ему самому, будь у него
            // своя такая же фабрика.
            var payment = quantity * unitCost;
            seller.Cash += payment;
            buyer.Cash -= payment;
            transferredTotal += quantity;
        }

        return transferredTotal;
    }

    /// <summary>
    /// Продаёт продавцу-ветке то, что не забрали соседи, системе по рыночной котировке этого хода
    /// (<see cref="MarketSaleCalculator"/>, с наценкой уровня передела и понижающим коэффициентом за
    /// превышение ёмкости) — аналог <c>SimpleBot.SellSurplusToSystem</c> реального бота (см.
    /// doc-comment класса). Материал у каждой ветки свой (<see cref="Material.Sector"/>), поэтому
    /// разные ветки никогда не делят одну и ту же ёмкость рынка за один вызов.
    /// </summary>
    private static void SellRemainingSurplusToSystem(
        BranchState seller, Material material, decimal remainingSurplus, ResolvedGameConfig config, Market market)
    {
        if (remainingSurplus <= 0m || !market.HasQuote(material.Id))
        {
            return;
        }

        var sale = MarketSaleCalculator.Calculate(market, config.Raw.Economy, material, remainingSurplus);
        var soldVolume = sale.WithinCapacityVolume + sale.OverflowVolume;
        if (soldVolume <= 0m)
        {
            return;
        }

        seller.Team.Warehouse.Remove(material, soldVolume);
        seller.Cash += sale.TotalRevenue;
        market.RecordSale(material.Id, soldVolume);
    }

    /// <summary>
    /// Остаток материала, который ветка производит сама, сверх желаемого расхода собственных
    /// рецептов, использующих его как вход, — то, что реально можно отдать соседям на этот ход.
    /// «Желаемый» — по теоретическому потолку выпуска фабрики-потребителя (<see
    /// cref="ProductionCalculator.CalculateCapacityBreakdown"/>, вход не в счёт — этот самый расчёт и
    /// призван его восполнить), не постоянная эвристика на 1-2 варки, как у бота (Блок 7.3.1,
    /// <c>SimpleBot</c>): идеальный зал видит будущее наперёд и знает точный желаемый темп, ему не
    /// нужен предохранительный запас на неопределённость.
    /// </summary>
    private static decimal ComputeSurplus(BranchState branch, Material material, ResolvedGameConfig config)
    {
        if (!branch.Team.Factories.Any(f => f.SelectedRecipe.Output == material))
        {
            return 0m;
        }

        var ownUse = SumDesiredInputQuantity(branch, material, config);
        return branch.Team.Warehouse.QuantityOf(material) - ownUse;
    }

    /// <summary>Нехватка материала, который ветка не производит сама, но который нужен как вход одному из её рецептов, — до желаемого темпа, см. doc-comment <see cref="ComputeSurplus"/>.</summary>
    private static decimal ComputeDeficit(BranchState branch, Material material, ResolvedGameConfig config)
    {
        if (branch.Team.Factories.Any(f => f.SelectedRecipe.Output == material))
        {
            return 0m;
        }

        var needed = SumDesiredInputQuantity(branch, material, config);
        return Math.Max(0m, needed - branch.Team.Warehouse.QuantityOf(material));
    }

    /// <summary>
    /// Сколько материала желали бы потребить за этот ход все фабрики ветки, использующие его как
    /// вход, если бы сырья хватало сколько угодно, — сумма по всем таким фабрикам их желаемого числа
    /// варок (потолок выпуска / выход рецепта за варку) на количество входа за варку.
    /// </summary>
    private static decimal SumDesiredInputQuantity(BranchState branch, Material material, ResolvedGameConfig config)
    {
        var total = 0m;
        foreach (var factory in branch.Team.Factories)
        {
            var input = factory.SelectedRecipe.Inputs.FirstOrDefault(i => i.Material == material);
            if (input is null)
            {
                continue;
            }

            var breakdown = ProductionCalculator.CalculateCapacityBreakdown(factory, config.Raw.WorkerProductivity, config.Raw.Rnd);
            var desiredBatches = breakdown.TheoreticalMaxOutput / factory.SelectedRecipe.OutputQuantity;
            total += desiredBatches * input.Quantity;
        }

        return total;
    }

    /// <summary>X(t) на конец хода — тот же состав слагаемых, что <see cref="FinalScoreCalculator"/>: касса + ликвидационная стоимость фабрик + ликвидационная стоимость склада по базовой рыночной цене (без наценки передела — она относится к активной продаже системе, не к пассивной оценке остатка, тот же принцип, что и в <see cref="FinalScoreCalculator.WarehouseValue"/>).</summary>
    private static decimal ComputeValue(
        BranchState branch, ResolvedGameConfig config, IReadOnlyDictionary<string, decimal> basePriceByMaterialId)
    {
        var factoriesValue = branch.Team.Factories.Sum(factory =>
        {
            var definition = config.Raw.FactoryDefinitions.First(d => d.Id == factory.Definition.Id);
            return definition.BuildCost * definition.LiquidationValueCoefficient;
        });

        var warehouseValue = branch.Team.Warehouse.Stock.Sum(stock =>
            stock.Quantity
            * basePriceByMaterialId.GetValueOrDefault(stock.Material.Id, 0m)
            * config.Raw.Economy.WarehouseLiquidationRate);

        return branch.Cash + factoriesValue + warehouseValue;
    }

    private static IReadOnlyDictionary<Material, decimal> BuildRawMaterialCosts(ResolvedGameConfig config)
    {
        var costs = new Dictionary<Material, decimal>();
        foreach (var entry in config.Raw.Economy.BaseMarketPerMaterial)
        {
            if (config.Materials.TryGetValue(entry.MaterialId, out var material) && material.IsRawMaterial)
            {
                costs[material] = entry.BasePrice;
            }
        }

        return costs;
    }

    /// <summary>Обёртка над <see cref="CostCalculator.CalculateUnitCost"/>, не падающая при отсутствующей базовой цене где-то в цепочке (тот же приём, что <c>SimpleBot.TryCalculateUnitCost</c>) — перевод в этот ход просто не считается, а не роняет весь прогон.</summary>
    private static bool TryCalculateUnitCost(
        Material material, RecipeBook recipeBook, IReadOnlyDictionary<Material, decimal> rawMaterialCosts, out decimal unitCost)
    {
        try
        {
            unitCost = CostCalculator.CalculateUnitCost(material, recipeBook, rawMaterialCosts);
            return true;
        }
        catch (ArgumentException)
        {
            unitCost = 0m;
            return false;
        }
    }
}
