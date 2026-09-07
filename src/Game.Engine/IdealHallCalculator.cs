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
/// <item>Обмен между ветками — по себестоимости (<see cref="MaterialCostCalculator"/> — не рыночная
/// котировка, запрос пользователя, rebalance/2-sector-stepwise, 2026-08-21), без переговорной надбавки;
/// платёж за перевод идёт в обе стороны (продавец получает деньги, покупатель платит) — перевод не
/// бесплатный подарок, просто без монопольной наценки.</item>
/// <item>Остаток излишка материала, который не забрала ни одна соседняя ветка (после <see
/// cref="TransferAcrossBranches"/>), продаётся системе тем же ходом по <see
/// cref="MarketSaleCalculator"/> — по правилам действующей модели ценообразования, включая просадку
/// цены за перепроизводство (собственный счётчик давления предложения, блок 11.6) — аналог
/// <c>SimpleBot.SellSurplusToSystem</c> у реального бота, а не только пассивная оценка склада в конце
/// хода (см. <see cref="ComputeValue"/>). Добавлено намеренно: без этого X(t) сильно
/// недооценивал ветки с большим числом параллельных нисходящих переделов на одном сырье — у них
/// заметная доля выпуска не находит покупателя среди соседних веток и должна уходить в реальный
/// рыночный доход, а не лежать на складе по неполной цене (см. <c>docs/TODO.md</c> №2, находка сессии
/// 2026-08-15).</item>
/// <item>Полная информация, ноль ошибок: капремонт не нужен вовсе — состояние фабрики держится на 1.0
/// (не моделируем износ), эквивалент «капремонт всегда точно вовремя». Это ЕДИНСТВЕННАЯ статья
/// реальных расходов, которой здесь нет: зарплата, содержание, электричество, наём и плата за
/// превышение склада списываются теми же формулами, что в реальном тике. До 2026-09-06 не списывались
/// также электричество, наём и склад — из-за чего X(t) был не верхней границей, а фикцией (на боевом
/// `metallurgy.json` неучтённым оставалось 73% реальных расходов, из них электричество —
/// крупнейшая статья вообще; см. `docs/economy-accounting-audit.md`, дефект 2).</item>
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
    /// Приёмник построчной трассировки расчёта (Блок «трассировка ботов», rebalance/2-sector-stepwise,
    /// диагностика для <c>--mode trace</c> в <c>Game.Balancing</c>) — <c>null</c> по умолчанию, тогда
    /// <see cref="Calculate"/> остаётся чистой функцией без побочных эффектов, как и было. Не
    /// потокобезопасно и не переиспользуется параллельно (единственный вызывающий — CLI-режим
    /// трассировки, который считает X(t) один раз последовательно) — статическое поле, а не параметр
    /// <see cref="Calculate"/>, чтобы не менять сигнатуру уже вызывающего кода, который трассировку не
    /// просит (грид/обычный <c>--mode ideal-hall</c>).
    /// </summary>
    public static Action<string>? Trace;

    /// <summary>Печатает склад и кассу ветки в лог трассировки (см. <see cref="Trace"/>) — сырьё для сравнения с трассировкой реального бота построчно.</summary>
    private static void TraceWarehouse(BranchState branch, string phase)
    {
        if (Trace is null)
        {
            return;
        }

        var stock = string.Join(" ", branch.Team.Warehouse.Stock.Select(s => $"{s.Material.Id}={s.Quantity:F1}"));
        Trace($"[ideal] {branch.Sector.Id,-8} {phase,-14} cash={branch.Cash,10:F1} {stock}");
    }

    /// <summary>
    /// Считает X(t) для каждого сектора <paramref name="config"/> на <paramref name="maxTurns"/>
    /// ходов. Чистая функция (без рандома) — два вызова с одним и тем же конфигом дают одно и то же
    /// (если не считать <see cref="Trace"/> — побочный эффект только на приёмник лога, не на сам результат).
    /// </summary>
    public static IdealHallResult Calculate(ResolvedGameConfig config, int maxTurns)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (maxTurns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTurns), maxTurns, "Turn count must be positive.");
        }

        var materialCosts = MaterialCostCalculator.CalculateAll(config);
        // Эталон обязан уметь то же, что и реальная команда (иначе он перестаёт быть верхней границей):
        // расшивать узкое место доньмом и второй фабрикой того же уровня — см. ChainCapacityPlanner.
        var capacityPlan = ChainCapacityPlanner.Plan(config);
        var referencePrices = SystemSaleReferencePriceCalculator.CalculateAll(config, materialCosts);
        var branches = config.Sectors.Select(sector => CreateBranch(config, sector)).ToList();
        var market = new Market();

        // Собственный счётчик давления предложения — зал симулирует, а не играет, журнала у него нет
        // (блок 11.6, долг из 11.5). Эквивалентен MarketSupplyPressureCalculator по построению:
        // затухание на 0.5^(1/полураспад) раз в ход плюс продажи текущего хода с полным весом дают
        // ровно ту же взвешенную сумму, что и обход журнала. Без него зал систематически завышал бы
        // выручку под External, не видя межходовой памяти рынка.
        var supplyPressure = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var pressureDecayPerTurn = (decimal)Math.Pow(
            0.5, 1.0 / Math.Max(1, config.Raw.Economy.MarketSupplyPressureHalfLifeTurns));

        for (var turn = 1; turn <= maxTurns; turn++)
        {
            Trace?.Invoke($"=== TURN {turn} ===");
            var marketUpdate = MarketCalculator.Calculate(turn, config.Raw.Economy);
            market.ReplaceQuotes(marketUpdate.Quotes, marketUpdate.ElectricityPrice, EconomyIndexCalculator.Calculate(turn, config.Raw.Economy));

            foreach (var materialId in supplyPressure.Keys.ToList())
            {
                supplyPressure[materialId] *= pressureDecayPerTurn;
            }

            foreach (var branch in branches)
            {
                AdvanceGeneration(branch, config, turn);
                ChargeGenerationResearch(branch, config);
                ChargeWarehouseFee(branch, config);
                BuildNewlyUnlockedFactories(branch, config, turn, maxTurns, capacityPlan, referencePrices);
                AdvanceFactoryLevelsAndChargeRnd(branch, config, turn);
                RunProduction(branch, config);
                ChargeOperatingCosts(branch, config);
                TraceWarehouse(branch, "post-production");
            }

            TransferAcrossBranches(branches, config, materialCosts, market, supplyPressure);

            foreach (var branch in branches)
            {
                TraceWarehouse(branch, "post-transfer");
                branch.ValueByTurn.Add(ComputeValue(branch, config, materialCosts));
            }
        }

        return new IdealHallResult
        {
            Branches = branches.Select(b => new IdealHallBranchTrajectory
            {
                SectorId = b.Sector.Id,
                SectorName = b.Sector.Name,
                ValueByTurn = b.ValueByTurn,
                ExpensesByType = b.Expenses,
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

        /// <summary>Накопленный расход по категориям — см. <see cref="IdealHallBranchTrajectory.ExpensesByType"/>.</summary>
        public Dictionary<FinanceHistoryCalculator.OperationType, decimal> Expenses { get; } = new();

        /// <summary>Списывает <paramref name="amount"/> с кассы и записывает его в категорию <paramref name="type"/> — единственный способ потратить деньги в этом классе, чтобы разбивка не могла разойтись с кассой.</summary>
        public void Spend(FinanceHistoryCalculator.OperationType type, decimal amount)
        {
            if (amount == 0m)
            {
                return;
            }

            Cash -= amount;
            Expenses[type] = Expenses.GetValueOrDefault(type) + amount;
        }
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
            branch.Spend(FinanceHistoryCalculator.OperationType.GenerationResearchInvested, genConfig.MaxCommitmentPerTurn);
        }

        branch.PreviousGeneration = branch.Team.UnlockedGeneration;
    }

    /// <summary>
    /// Единица достройки — пара (тип, рецепт), не сам тип (тот же принцип и то же обоснование, что и
    /// у <see cref="SimpleBot.BuildNewlyUnlockedFactories"/>, тем же именем не просто совпадение —
    /// оба должны сходиться в одном и том же выборе рецепта, иначе «идеальный зал» перестаёт быть
    /// честной верхней границей для реального бота, запрос пользователя, TODO.md #20, 2026-08-17):
    /// тип с несколькими рецептами даёт отдельную фабрику на каждый рецепт. Сколько ИМЕННО фабрик
    /// каждой пары и по сколько рабочих на каждой — решает <see cref="ChainCapacityPlanner"/>, а не
    /// «одна с базовой численностью» (2026-09-07): эталон обязан уметь расшивать узкое место теми же
    /// рычагами, что и живая команда, иначе он перестаёт быть верхней границей для бота, который это
    /// теперь умеет.
    ///
    /// <para>
    /// <b>Гейт «успеет ли отбить хотя бы наём» (2026-09-07, docs/TODO.md №29).</b> Раньше зал строил
    /// каждую пару в тот же ход, когда её разблокировало поколение, безусловно — «раньше не может быть
    /// хуже, чем позже». После починки учёта (docs/economy-accounting-audit.md, дефект 2) зал платит
    /// за наём при постройке; фабрика, разблокированная за один-два хода до конца партии, этот
    /// разовый расход уже не отобьёт своим переделом и тянет итог вниз. Такая пара теперь не строится.
    /// </para>
    /// <para>
    /// Порог намеренно узкий — <i>только</i> наём, не полный <c>BuildCost</c>. Сам <c>BuildCost</c>
    /// возвращается в итог остаточной стоимостью фабрики при <c>Condition=1</c>
    /// (<see cref="FinalScoreCalculator"/>), поэтому построить фед-фабрику на любое число ходов &gt; ~2
    /// строго улучшает счёт на <c>передел×0.30×ходы − наём</c> — гейт по «окупаемости всего BuildCost»
    /// (первый вариант из №29) отсекал бы фабрики, которые реальному боту всё равно выгодно строить, и
    /// сам ломал бы верхнюю границу. Остаточное превышение бота над залом на короткой синтетической
    /// цепочке (<c>IdealHallUpperBoundTests</c>, ~102%) этот гейт не закрывает — оно от фронт-загрузки
    /// капзатрат и темпа вложений на 1-м ходу, а это уже «полноценный решатель по ходам», второй
    /// вариант №29, отложенный.
    /// </para>
    /// </summary>
    private static void BuildNewlyUnlockedFactories(
        BranchState branch, ResolvedGameConfig config, int turn, int maxTurns,
        IReadOnlyDictionary<(string FactoryDefinitionId, string RecipeId), ChainCapacityPlanner.RecipePlan> capacityPlan,
        IReadOnlyDictionary<string, decimal> referencePrices)
    {
        var turnsRemaining = maxTurns - turn + 1;
        var builtCountByPair = branch.Team.Factories
            .GroupBy(f => (f.Definition.Id, f.SelectedRecipe.Id))
            .ToDictionary(group => group.Key, group => group.Count());
        foreach (var definition in branch.SectorFactories)
        {
            foreach (var recipe in definition.Recipes)
            {
                if (recipe.Output.Level > branch.Team.UnlockedGeneration)
                {
                    continue;
                }

                var plan = capacityPlan[(definition.Id, recipe.Id)];
                var alreadyBuilt = builtCountByPair.GetValueOrDefault((definition.Id, recipe.Id));
                var rawDefinition = config.Raw.FactoryDefinitions.First(d => d.Id == definition.Id);
                var buildCost = rawDefinition.BuildCost;

                if (alreadyBuilt == 0
                    && !WouldRecoverHireCostBeforeGameEnds(
                        definition, recipe, plan.WorkersPerFactory, rawDefinition.FixedCostPerTurn, config, turnsRemaining,
                        referencePrices))
                {
                    // Даже разовый наём не отобьёт — пропускаем навсегда: с ростом turn окно только сужается.
                    continue;
                }

                for (var i = alreadyBuilt; i < plan.FactoryCount; i++)
                {
                    var factory = branch.Team.BuildFactory(Ulid.NewUlid(), definition, recipe, builtAtTurn: turn);
                    factory.Hire(plan.WorkersPerFactory);
                    branch.Spend(FinanceHistoryCalculator.OperationType.FactoryBuilt, buildCost);
                    // Наём тоже стоит денег (docs/economy-accounting-audit.md, дефект 2, шаг 2) — раньше
                    // идеальный зал набирал бригаду бесплатно, реальная команда платит.
                    branch.Spend(
                        FinanceHistoryCalculator.OperationType.WorkersHired,
                        plan.WorkersPerFactory * config.Raw.WorkerProductivity.HireCostPerWorker);
                    branch.BuiltAtTurn[factory.Id] = turn;
                    branch.PreviousLevel[factory.Id] = 1;
                }
            }
        }
    }

    /// <summary>
    /// Отобьёт ли пара (тип, рецепт), построенная сейчас, хотя бы свой разовый наём за оставшиеся
    /// <paramref name="turnsRemaining"/> ходов. Прибыль за ход — общая формула
    /// <see cref="SystemSaleReferencePriceCalculator.ProfitPerTurn"/> (выпуск по опорной цене минус
    /// входы по их опорной цене минус собственный передел) на свежепостроенной фабрике первого
    /// уровня при <paramref name="workersPerFactory"/> рабочих: те же допущения, что у
    /// <c>ProductionCostLevelCalculator</c> в Game.Balancing — 100% выпуска системе, без
    /// кросс-торговли и без роста выпуска от R&amp;D, то есть заведомо не оптимистичная. Возвращает
    /// <c>false</c>, если прибыль не положительна или <c>прибыль × turnsRemaining</c> меньше разового
    /// <c>HireCostPerWorker × workersPerFactory</c>.
    /// </summary>
    private static bool WouldRecoverHireCostBeforeGameEnds(
        FactoryDefinition definition, Recipe recipe, int workersPerFactory,
        decimal fixedCostPerTurn, ResolvedGameConfig config, int turnsRemaining,
        IReadOnlyDictionary<string, decimal> referencePrices)
    {
        var probe = new Factory(Ulid.NewUlid(), definition.Sector, definition, recipe);
        probe.Hire(workersPerFactory);
        var output = ProductionCalculator
            .CalculateCapacityBreakdown(probe, config.Raw.WorkerProductivity, config.Raw.Rnd)
            .TheoreticalMaxOutput;

        var electricityCost = output
            * config.Raw.Economy.ElectricityConsumptionPerOutputUnit
            * config.Raw.Economy.ElectricityBasePrice;
        var salaryCost = workersPerFactory * config.Raw.WorkerProductivity.SalaryPerWorkerPerTurn;
        var conversionCost = fixedCostPerTurn + electricityCost + salaryCost;
        var batches = recipe.OutputQuantity > 0 ? output / recipe.OutputQuantity : 0m;
        var inputsAtReferencePrice = recipe.Inputs.Sum(
            input => input.Quantity * batches * SystemSaleReferencePriceCalculator.PriceOf(referencePrices, input.Material.Id));
        var profitPerTurn = SystemSaleReferencePriceCalculator.ProfitPerTurn(
            output,
            SystemSaleReferencePriceCalculator.PriceOf(referencePrices, recipe.Output.Id),
            inputsAtReferencePrice,
            conversionCost);
        if (profitPerTurn <= 0m)
        {
            return false;
        }

        var hireCost = workersPerFactory * config.Raw.WorkerProductivity.HireCostPerWorker;
        return profitPerTurn * turnsRemaining >= hireCost;
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
                branch.Spend(FinanceHistoryCalculator.OperationType.RndInvested, rndConfig.MaxCommitmentPerTurn);
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

                // Электричество — та же формула и та же (базовая) цена, что списывает реальный тик
                // (GameSession.RunTick). Раньше не списывалось вовсе, хотя это крупнейшая статья
                // расходов на реальных конфигах — из-за чего X(t) был не потолком, а фикцией
                // (docs/economy-accounting-audit.md, дефект 2, шаг 2).
                branch.Spend(
                    FinanceHistoryCalculator.OperationType.FactoryOverhead,
                    result.OutputQuantity
                        * config.Raw.Economy.ElectricityConsumptionPerOutputUnit
                        * config.Raw.Economy.ElectricityBasePrice);
            }
        }
    }

    /// <summary>
    /// Зарплата и содержание фабрик — те же формулы, что реальный тик (<see cref="FinanceCalculator"/>);
    /// состояние всех фабрик — 1.0 (см. doc-comment класса), поэтому штрафа за износ в содержании нет.
    /// Электричество списывает <see cref="RunProduction"/> (там известен выпуск), наём —
    /// <see cref="BuildNewlyUnlockedFactories"/>, плату за склад — <see cref="ChargeWarehouseFee"/>.
    /// Единственная статья реальных расходов, которой у идеального зала нет вовсе, — капремонт
    /// (следствие допущения «износа нет», <c>docs/TODO.md</c> №18).
    /// </summary>
    private static void ChargeOperatingCosts(BranchState branch, ResolvedGameConfig config)
    {
        var totalWorkers = branch.Team.Factories.Sum(f => f.Workers);
        branch.Spend(
            FinanceHistoryCalculator.OperationType.SalariesPaid,
            FinanceCalculator.CalculateSalaries(totalWorkers, config.Raw.WorkerProductivity));
        branch.Spend(
            FinanceHistoryCalculator.OperationType.FactoryUpkeep,
            FinanceCalculator.CalculateFactoryUpkeep(branch.Team.Factories, config.Raw.FactoryDefinitions, config.Raw.Wear));
    }

    /// <summary>
    /// Плата за превышение бесплатного лимита склада (<see cref="WarehouseFeeCalculator"/>) — как и в
    /// реальном тике, считается по остатку НА НАЧАЛО хода, до производства и до продажи излишка
    /// (<see cref="GameSession.RunTick"/> зовёт <see cref="TickFinanceStep"/> первым, раньше
    /// системной продажи и производства). Списывать её после производства было бы строже реального
    /// движка: платили бы за выпуск, который тем же ходом уходит системе.
    /// </summary>
    private static void ChargeWarehouseFee(BranchState branch, ResolvedGameConfig config)
    {
        var totalStock = branch.Team.Warehouse.Stock.Sum(stock => stock.Quantity);
        branch.Spend(
            FinanceHistoryCalculator.OperationType.WarehouseFee,
            WarehouseFeeCalculator.Calculate(totalStock, config.Raw.Warehouse).Fee);
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
        IReadOnlyList<BranchState> branches, ResolvedGameConfig config, IReadOnlyDictionary<string, decimal> materialCosts,
        Market market, Dictionary<string, decimal> supplyPressure)
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

            var transferred = TransferToBuyers(seller, branches, material, surplus, config, materialCosts);
            var remainingSurplus = surplus - transferred;
            Trace?.Invoke($"[ideal] transfer {material.Id,-12} продавец={seller.Sector.Id} излишек={surplus:F1} передано={transferred:F1} продано системе={remainingSurplus:F1}");
            SellRemainingSurplusToSystem(seller, material, remainingSurplus, config, market, materialCosts, supplyPressure);
        }
    }

    /// <summary>Раздаёт излишек продавца соседним веткам-покупателям по себестоимости (см. doc-comment <see cref="TransferAcrossBranches"/>). Возвращает фактически переданное количество — остаток после этого не покупателям, а системе (<see cref="SellRemainingSurplusToSystem"/>).</summary>
    private static decimal TransferToBuyers(
        BranchState seller, IReadOnlyList<BranchState> branches, Material material, decimal surplus,
        ResolvedGameConfig config, IReadOnlyDictionary<string, decimal> materialCosts)
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
        Trace?.Invoke(fillRatio < 1m
            ? $"[ideal] transfer {material.Id,-12} покупатели=[{string.Join(", ", buyers.Select(b => $"{b.Branch.Sector.Id}:дефицит={b.Deficit:F1}"))}] fillRatio={fillRatio:P0} — излишка не хватает на весь спрос"
            : $"[ideal] transfer {material.Id,-12} покупатели=[{string.Join(", ", buyers.Select(b => $"{b.Branch.Sector.Id}:дефицит={b.Deficit:F1}"))}] fillRatio={fillRatio:P0}");
        if (fillRatio <= 0m || !materialCosts.TryGetValue(material.Id, out var unitCost))
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
            buyer.Spend(FinanceHistoryCalculator.OperationType.ContractDelivery, payment);
            transferredTotal += quantity;
        }

        return transferredTotal;
    }

    /// <summary>
    /// Продаёт продавцу-ветке то, что не забрали соседи, системе по себестоимости этого материала
    /// (<see cref="MarketSaleCalculator"/>, с наценкой уровня передела и понижающим коэффициентом за
    /// превышение ёмкости) — аналог <c>SimpleBot.SellSurplusToSystem</c> реального бота (см.
    /// doc-comment класса). Материал у каждой ветки свой (<see cref="Material.Sector"/>), поэтому
    /// разные ветки никогда не делят одну и ту же ёмкость рынка за один вызов.
    /// </summary>
    private static void SellRemainingSurplusToSystem(
        BranchState seller, Material material, decimal remainingSurplus, ResolvedGameConfig config, Market market,
        IReadOnlyDictionary<string, decimal> materialCosts, Dictionary<string, decimal> supplyPressure)
    {
        if (remainingSurplus <= 0m || !market.HasQuote(material.Id))
        {
            return;
        }

        var pressureBefore = supplyPressure.GetValueOrDefault(material.Id);
        var sale = MarketSaleCalculator.Calculate(
            market, materialCosts, config.Raw.Economy, material, remainingSurplus, pressureBefore);
        var soldVolume = sale.WithinCapacityVolume + sale.OverflowVolume;
        if (soldVolume <= 0m)
        {
            return;
        }

        seller.Team.Warehouse.Remove(material, soldVolume);
        seller.Cash += sale.TotalRevenue;
        market.RecordSale(material.Id, soldVolume);
        supplyPressure[material.Id] = pressureBefore + soldVolume;
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

    /// <summary>
    /// X(t) на конец хода — тот же состав слагаемых, что <see cref="FinalScoreCalculator"/>: касса +
    /// остаточная стоимость фабрик (привязана к <see cref="Factory.Condition"/> — у идеального зала
    /// он всегда 1.0, износ не моделируется, «капремонт всегда точно вовремя», см. doc-comment класса,
    /// поэтому здесь фабрики всегда стоят полную <see cref="FactoryDefinitionConfig.BuildCost"/>, не
    /// долю от неё) + остаточная стоимость склада **ровно по себестоимости**, без скидки на ликвидацию
    /// (<c>EconomyConfig.WarehouseLiquidationRate</c> здесь больше не участвует — сознательное
    /// упрощение по запросу пользователя, одна и та же формула для идеального зала, реального бота и
    /// будущей реальной игры, см. doc-comment <see cref="FinalScoreCalculator"/>).
    /// </summary>
    private static decimal ComputeValue(
        BranchState branch, ResolvedGameConfig config, IReadOnlyDictionary<string, decimal> materialCosts)
    {
        var factoriesValue = branch.Team.Factories.Sum(factory =>
        {
            var definition = config.Raw.FactoryDefinitions.First(d => d.Id == factory.Definition.Id);
            return FactoryResidualValueCalculator.Calculate(definition, factory.Condition);
        });

        var warehouseValue = branch.Team.Warehouse.Stock.Sum(stock =>
            stock.Quantity * materialCosts.GetValueOrDefault(stock.Material.Id, 0m));

        return branch.Cash + factoriesValue + warehouseValue;
    }
}
