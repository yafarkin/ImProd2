using Game.Config;
using Game.Config.Economy;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Balancing.Tests;

/// <summary>
/// Сверка идеального зала с настоящим тиком движка **по каждой статье расходов** — единственная
/// проверка, которая ловит класс ошибок, стоивший проекту нескольких недель отладки
/// (<c>docs/economy-accounting-audit.md</c>, 2026-09-06).
///
/// <para>
/// <b>Зачем она нужна и почему её не заменяет ничто из §1-§4 диагностики.</b> Три дефекта учёта
/// (электричество билось по дрейфующей рыночной цене, а продавалось по статичной базовой; идеальный
/// зал вообще не списывал электричество, наём, склад и капремонт; калькулятор окупаемости считал
/// прибыль по неверной формуле) были невидимы для любой проверки конфига, потому что все инструменты
/// делили один и тот же неверный код и согласованно показывали неверное. Согласие инструментов,
/// которые делят код, не доказывает ничего — нужен независимый источник истины, и здесь им выступает
/// журнал реального движка, разобранный <see cref="FinanceHistoryCalculator"/>.
/// </para>
///
/// <para>
/// <b>Как устроено выравнивание.</b> Идеальный зал строит фабрики в начале хода и тем же ходом
/// производит; движок принимает решения в фазе решений хода N, а работают они начиная с расчёта хода
/// N+1. Поэтому сценарий на движке идёт на один ход дольше, а сравниваются НАКОПЛЕННЫЕ итоги за весь
/// прогон, не поход`овые: постройка и наём случаются по разу с обеих сторон, а зарплата/содержание/
/// электричество набегают одинаковое число раз (зал — ходы 1..T, движок — 2..T+1).
/// </para>
///
/// <para>
/// <b>Износ и капремонт сверяются отдельным сценарием</b> (<see
/// cref="Wear_And_Overhaul_Expenses_Match_The_Engine_Under_The_Same_Maintenance_Policy"/>, 2026-09-08,
/// <c>docs/TODO.md</c> №18): в конфиге по умолчанию износ выключен (<c>GracePeriodTurns = 1000</c> у
/// <see cref="SyntheticChainConfigBuilder"/>), поэтому основной сценарий его не касается вовсе.
/// </para>
///
/// <para>
/// <b>Что специально выведено за скобки.</b> Аварийная закупка — зал
/// по построению никогда не остаётся без сырья. Доходы (продажа системе) не сверяются: зал продаёт
/// излишек каждый ход, а сценарий на движке ничего не продаёт, чтобы не тащить в проверку ещё и
/// поведение бота. Это осознанное ограничение: все три исторических дефекта были в РАСХОДАХ, и
/// именно расходы здесь и сверяются.
/// </para>
/// </summary>
public class IdealHallEngineReconciliationTests
{
    // Прогон должен быть длиннее, чем окупаемость разового найма самого мелкого уровня цепочки
    // (≈29 ходов при этих числах) — иначе гейт «успеет ли отбить наём» (docs/TODO.md №29,
    // IdealHallCalculator.BuildNewlyUnlockedFactories) законно не даст залу построить фабрику, и
    // сверять расходы будет не с чем. До добавления гейта (2026-09-07) здесь стояло 8.
    private const int HallTurns = 40;

    /// <summary>
    /// Длина прогона для сценария с износом — не 40, и это не косметика. Зал строит, нанимает и
    /// производит одним и тем же ходом (ход 1), а движок на ходу постройки ещё не производит (рабочие
    /// выходят на смену расчётом того же хода) — поэтому окна сравнения сдвинуты на ход: зал считает
    /// 1..T, движок 2..T+1. Пока состояние фабрик держалось на 1.0, сдвиг ничего не стоил: все ходы
    /// одинаковые. С износом ходы перестали быть одинаковыми — «лишний» ход зала (1-й) и «лишний» ход
    /// движка (T+1) обязаны быть одной фазы цикла, иначе одна из сторон получит на один ход
    /// пониженного выпуска больше. Цикл при <c>GracePeriodTurns = 3</c> равен 6 ходам (4 полных, 1
    /// изношенный, 1 ремонтный), 1-й ход — полный; значит T+1 тоже обязан быть полным:
    /// 43 mod 6 = 1 ✅, тогда как 41 mod 6 = 5 — попадает ровно на изношенный ход.
    /// </summary>
    private const int WearScenarioTurns = 42;

    /// <summary>
    /// Здоровая цепочка (предложение каждого уровня с запасом покрывает спрос следующего), поэтому
    /// производство и у зала, и у движка упирается в мощность, а не в наличие сырья — только при этом
    /// условии выпуск с обеих сторон совпадает и электричество вообще сравнимо. Числа — из реального
    /// фикса направления B, как и у <c>IdealHallUpperBoundTests</c>.
    /// </summary>
    private static readonly SyntheticChainConfigBuilder.LevelParams[] Levels =
    [
        new(BuildCost: 350m, FixedCostPerTurn: 6.67m, ProductionRate: 50m),
        new(BuildCost: 800m, FixedCostPerTurn: 20m, ProductionRate: 20m),
        new(BuildCost: 1650m, FixedCostPerTurn: 46.67m, ProductionRate: 7.5m),
    ];

    [Fact]
    public void Every_Expense_Category_Matches_The_Engine_Ledger_On_The_Same_Scenario()
    {
        var config = BuildConfig();

        var hall = IdealHallCalculator.Calculate(config, maxTurns: HallTurns);
        var hallExpenses = hall.Branches.Single().ExpensesByType;
        var engineExpenses = RunScriptedEngineScenario(config);

        // Набор статей обязан совпадать буквально: статья, которую зал молча не списывает, — это
        // ровно дефект 2 из разбора, и она обязана здесь всплыть как отсутствующий ключ, а не
        // раствориться в итоговом X(t).
        Assert.Equal(
            engineExpenses.Keys.OrderBy(k => k.ToString(), StringComparer.Ordinal).ToList(),
            hallExpenses.Keys.OrderBy(k => k.ToString(), StringComparer.Ordinal).ToList());

        foreach (var (type, engineAmount) in engineExpenses.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal))
        {
            var hallAmount = hallExpenses[type];
            Assert.True(
                Math.Abs(hallAmount - engineAmount) < 0.01m,
                $"Статья {type}: идеальный зал списал {hallAmount:F2}, движок — {engineAmount:F2} " +
                "на одном и том же сценарии. Либо зал считает по другой формуле/цене, либо движок " +
                "изменился, а зал не догнал — см. docs/economy-accounting-audit.md.");
        }
    }

    /// <summary>
    /// Отдельная проверка того самого дефекта 1: электричество списывается по <see
    /// cref="EconomyConfig.ElectricityBasePrice"/>, а не по дрейфующей котировке
    /// <c>State.Market.ElectricityPrice</c>. Конфиг здесь специально с трендом, уводящим цену вверх,
    /// и с длиной прогона, заведомо покрывающей фазу тренда — если биллинг снова привяжут к рыночной
    /// цене, суммы разойдутся ровно на величину дрейфа.
    /// </summary>
    [Fact]
    public void Electricity_Is_Billed_At_The_Base_Price_The_Cost_Model_Uses_Not_The_Drifting_Quote()
    {
        var config = BuildConfig();
        Assert.NotEmpty(config.Raw.Economy.TrendScenario);

        var engineExpenses = RunScriptedEngineScenario(config, out var producedUnits, out var driftedPrice);

        Assert.True(driftedPrice > config.Raw.Economy.ElectricityBasePrice, "Тренд обязан реально увести котировку от базы, иначе проверка холостая.");

        var expected = producedUnits * config.Raw.Economy.ElectricityConsumptionPerOutputUnit * config.Raw.Economy.ElectricityBasePrice;
        var actual = engineExpenses[FinanceHistoryCalculator.OperationType.FactoryOverhead];
        Assert.True(
            Math.Abs(actual - expected) < 0.01m,
            $"Электричество: движок списал {actual:F2} за {producedUnits:F1} ед. выпуска, а по базовой цене " +
            $"{config.Raw.Economy.ElectricityBasePrice} должно быть {expected:F2}. Котировка на конец прогона — " +
            $"{driftedPrice}: похоже, биллинг снова привязан к ней, а не к базе (дефект 1).");
    }

    /// <summary>
    /// Разбивка по статьям обязана в точности объяснять движение кассы — иначе она была бы просто
    /// вторым, независимо ведущимся набором чисел, который может разъехаться с реальностью так же
    /// незаметно, как разъехались инструменты в исходной истории.
    /// </summary>
    [Fact]
    public void Expense_Breakdown_Fully_Explains_The_Engine_Cash_Movement()
    {
        var config = BuildConfig();
        var engineExpenses = RunScriptedEngineScenario(config, out _, out _, out var team, out var startingCash);

        var totalExpenses = engineExpenses.Values.Sum();
        Assert.Equal(startingCash - totalExpenses, team.Balance);
    }

    /// <summary>
    /// Та же сверка, но на конфиге с ВКЛЮЧЁННЫМ износом, и движок при этом обслуживает фабрики по той
    /// же эталонной политике, что и зал (капремонт при первом же отклонении от 1.0 — то есть всегда
    /// самая дешёвая ступень, как <c>SimpleBot.MaintainFactories</c> при
    /// <c>IgnoredCheapestTierCount = 0</c>). Это и есть проверка №18: до 2026-09-08 зал держал
    /// состояние на 1.0 навсегда, поэтому не платил ни за капремонт, ни за штраф к содержанию, и
    /// набор статей у него был заведомо беднее движкового.
    ///
    /// <para>
    /// Тайминг сходится сам собой, без подгонки: зал строит на ходу 1 (<c>LastResetTurn = 1</c>) и
    /// идёт 1..T, движок стартует в фазе расчёта, поэтому строит на ходу 2 (<c>LastResetTurn = 2</c>)
    /// и идёт 2..T+1 — тот же сдвиг на один ход, что и у остальных статей. Ремонт и там, и там
    /// начинается на следующий ход после декея: зал видит <c>Condition &lt; 1</c> в начале
    /// следующего хода, а движок узнаёт о декее из расчёта хода N и заказывает капремонт в фазу
    /// решений хода N+1.
    /// </para>
    /// </summary>
    [Fact]
    public void Wear_And_Overhaul_Expenses_Match_The_Engine_Under_The_Same_Maintenance_Policy()
    {
        var config = BuildConfigWithWear();

        var hall = IdealHallCalculator.Calculate(config, maxTurns: WearScenarioTurns);
        var hallExpenses = hall.Branches.Single().ExpensesByType;
        var engineExpenses = RunScriptedEngineScenario(config, out _, out _, out _, out _, maintainFactories: true, turns: WearScenarioTurns);

        // Проверка не должна быть холостой: если износ снова окажется выключён (или политика
        // перестанет заказывать ремонт), обе стороны просто не заплатят за капремонт и «сойдутся».
        Assert.True(
            hallExpenses.TryGetValue(FinanceHistoryCalculator.OperationType.FactoryOverhaul, out var hallOverhaul) && hallOverhaul > 0m,
            "Идеальный зал не заплатил за капремонт ни разу — значит износ в этом конфиге не включён и сверять нечего.");

        Assert.Equal(
            engineExpenses.Keys.OrderBy(k => k.ToString(), StringComparer.Ordinal).ToList(),
            hallExpenses.Keys.OrderBy(k => k.ToString(), StringComparer.Ordinal).ToList());

        foreach (var (type, engineAmount) in engineExpenses.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal))
        {
            var hallAmount = hallExpenses[type];
            Assert.True(
                Math.Abs(hallAmount - engineAmount) < 0.01m,
                $"Статья {type} на конфиге с износом: идеальный зал списал {hallAmount:F2}, движок — {engineAmount:F2}. " +
                "Либо политика обслуживания зала разошлась с ботовской, либо порядок износа внутри хода " +
                "(см. IdealHallCalculator.RunWearAndOverhaul против TickFinanceStep/WearStep).");
        }
    }

    /// <summary>
    /// Эталонная игра не доводит фабрику до принудительной остановки — прямое следствие политики
    /// «чинить при первом признаке износа». Проверяется не косвенно по деньгам, а по самому факту:
    /// состояние ни одной фабрики зала не проваливается ниже нижней границы самой дешёвой ступени.
    /// Сторожит подмену политики на «тянуть до последнего» (она экономила бы на числе ремонтов, но
    /// разошлась бы с ботом и обрушила бы X(t) просадкой выпуска).
    /// </summary>
    [Fact]
    public void Reference_Policy_Always_Repairs_At_The_Cheapest_Tier_And_Never_Reaches_Forced_Downtime()
    {
        var config = BuildConfigWithWear();
        var cheapestTier = config.Raw.Wear.OverhaulTiers[0];

        var engineExpenses = RunScriptedEngineScenario(
            config, out _, out _, out var team, out _, maintainFactories: true, out var overhaulTierIds, out var forcedRepairs,
            turns: WearScenarioTurns);

        Assert.NotEmpty(overhaulTierIds);
        Assert.All(overhaulTierIds, tierId => Assert.Equal(cheapestTier.Id, tierId));
        Assert.Empty(forcedRepairs);
        Assert.All(team.Factories, factory => Assert.True(
            factory.Condition >= cheapestTier.MinCondition,
            $"Состояние фабрики опустилось до {factory.Condition:F2} — ниже границы самой дешёвой ступени " +
            $"({cheapestTier.MinCondition:F2}), значит ремонт заказывался не при первом признаке износа."));
        Assert.True(engineExpenses[FinanceHistoryCalculator.OperationType.FactoryOverhaul] > 0m);
    }

    /// <summary>
    /// Тот же синтетический конфиг, но с включённым электричеством и трендом, уводящим его цену вверх
    /// (иначе главный исторический дефект нечем воспроизвести — в дефолте построителя расход
    /// электричества нулевой), и с недостижимыми за прогон порогами R&amp;D, чтобы вложения шли
    /// каждый ход у обеих сторон и не упирались в потолок на разных ходах.
    /// </summary>
    private static ResolvedGameConfig BuildConfig()
    {
        var raw = SyntheticChainConfigBuilder.Build(Levels, inputQuantityPerLevel: 2m).Raw;
        return GameConfigLoader.Load(raw with
        {
            Economy = raw.Economy with
            {
                ElectricityBasePrice = 2m,
                ElectricityConsumptionPerOutputUnit = 0.01m,
                TrendScenario =
                [
                    // Цена электричества дрейфует тем же PriceChangePerTurn, что и цены материалов
                    // (MarketCalculator.Calculate), и накопленный дрейф после конца фазы остаётся
                    // навсегда — именно на этом и держался дефект 1.
                    new EconomyTrendPhaseConfig
                    {
                        Trend = EconomyTrend.Up,
                        StartTurn = 1,
                        EndTurn = 5,
                        PriceChangePerTurn = 0.5m,
                        CapacityChangePerTurn = 0m,
                    },
                ],
            },
            Rnd = raw.Rnd with { ResearchPointThresholdsByLevel = [1_000_000m] },
            Warehouse = raw.Warehouse with { FreeCapacity = 100_000_000m },
        });
    }

    /// <summary>
    /// Тот же синтетический конфиг, но с реально работающим износом: льготный период укорочен с 1000
    /// ходов (в построителе он стоит именно для того, чтобы износ не мешал остальным проверкам) до
    /// трёх, так что за прогон успевает пройти несколько полных циклов «износ → капремонт →
    /// восстановление».
    /// </summary>
    private static ResolvedGameConfig BuildConfigWithWear()
    {
        var raw = BuildConfig().Raw;
        return GameConfigLoader.Load(raw with { Wear = raw.Wear with { GracePeriodTurns = 3 } });
    }

    private static IReadOnlyDictionary<FinanceHistoryCalculator.OperationType, decimal> RunScriptedEngineScenario(
        ResolvedGameConfig config) => RunScriptedEngineScenario(config, out _, out _, out _, out _);

    private static IReadOnlyDictionary<FinanceHistoryCalculator.OperationType, decimal> RunScriptedEngineScenario(
        ResolvedGameConfig config, out decimal producedUnits, out decimal finalElectricityPrice) =>
        RunScriptedEngineScenario(config, out producedUnits, out finalElectricityPrice, out _, out _);

    private static IReadOnlyDictionary<FinanceHistoryCalculator.OperationType, decimal> RunScriptedEngineScenario(
        ResolvedGameConfig config,
        out decimal producedUnits,
        out decimal finalElectricityPrice,
        out Team team,
        out decimal startingCash,
        bool maintainFactories,
        int turns = HallTurns) =>
        RunScriptedEngineScenario(config, out producedUnits, out finalElectricityPrice, out team, out startingCash, maintainFactories, out _, out _, turns);

    /// <summary>
    /// Повторяет на настоящем движке ровно то, что делает идеальный зал: строит те же фабрики теми же
    /// парами (тип, рецепт) в том же количестве и с той же численностью (<see
    /// cref="ChainCapacityPlanner"/> — общий источник и для зала, и здесь), объявляет тот же темп
    /// вложений (потолок каждый ход) и больше не делает ничего — ни продаж, ни контрактов, ни
    /// капремонта. Стартовый грант заведомо покрывает всё, чтобы отрицательный баланс не влиял ни на
    /// одно решение движка.
    /// </summary>
    private static IReadOnlyDictionary<FinanceHistoryCalculator.OperationType, decimal> RunScriptedEngineScenario(
        ResolvedGameConfig config,
        out decimal producedUnits,
        out decimal finalElectricityPrice,
        out Team team,
        out decimal startingCash) =>
        RunScriptedEngineScenario(config, out producedUnits, out finalElectricityPrice, out team, out startingCash, maintainFactories: false, out _, out _, HallTurns);

    private static IReadOnlyDictionary<FinanceHistoryCalculator.OperationType, decimal> RunScriptedEngineScenario(
        ResolvedGameConfig config,
        out decimal producedUnits,
        out decimal finalElectricityPrice,
        out Team team,
        out decimal startingCash,
        bool maintainFactories,
        out IReadOnlyList<string> overhaulTierIds,
        out IReadOnlyList<Ulid> forcedRepairs,
        int turns = HallTurns)
    {
        const decimal grant = 10_000_000m;
        var sector = config.Sectors.Single();
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config, endTurn: turns + 1, [new TeamSpec { Id = teamId, Name = "Сценарий", SectorId = sector.Id }]);
        session.GrantToTeam(teamId, grant);

        var plan = ChainCapacityPlanner.Plan(config);
        var random = new Random(1);
        var built = false;

        while (!session.State.IsFinished)
        {
            if (session.State.CurrentPhase == TurnPhase.Settlement)
            {
                session.RunTick(random);
                session.AdvancePhase(PhaseTransitionTrigger.Timer);
                continue;
            }

            if (!built)
            {
                foreach (var definition in config.FactoryDefinitions.Where(d => d.Sector == sector).OrderBy(d => d.Recipes[0].Output.Level))
                {
                    foreach (var recipe in definition.Recipes)
                    {
                        var recipePlan = plan[(definition.Id, recipe.Id)];
                        for (var i = 0; i < recipePlan.FactoryCount; i++)
                        {
                            var entry = session.BuildFactory(teamId, definition.Id, recipe.Id);
                            var factoryId = ((FactoryBuilt)entry.Change).FactoryId;
                            session.SetWorkerCount(teamId, factoryId, recipePlan.WorkersPerFactory);
                            session.SetRndCommitment(teamId, factoryId, config.Raw.Rnd.MaxCommitmentPerTurn);
                        }
                    }
                }

                session.SetGenerationResearchCommitment(teamId, config.Raw.GenerationResearch.MaxCommitmentPerTurn);
                built = true;
            }

            if (maintainFactories)
            {
                // Ровно SimpleBot.MaintainFactories при IgnoredCheapestTierCount = 0 и ровно та же
                // политика, что у IdealHallCalculator.RunWearAndOverhaul: чинить при первом же
                // отклонении от 1.0.
                foreach (var factory in session.State.Teams[teamId].Factories
                             .Where(f => !f.IsUnderRepair && !f.OverhaulRequested && f.Condition < 1m)
                             .ToList())
                {
                    session.SetOverhaulRequested(teamId, factory.Id, requested: true);
                }
            }

            session.AdvancePhase(PhaseTransitionTrigger.Timer);
        }

        overhaulTierIds = session.Entries.Select(e => e.Change).OfType<FactoryOverhaulStarted>().Select(c => c.TierId).ToList();
        forcedRepairs = session.Entries.Select(e => e.Change).OfType<FactoryEnteredRepair>().Select(c => c.FactoryId).ToList();
        team = session.State.Teams[teamId];
        startingCash = grant;
        finalElectricityPrice = session.State.Market.ElectricityPrice;
        producedUnits = session.Entries
            .Select(e => e.Change)
            .OfType<FactoryProduced>()
            .Sum(p => p.OutputQuantity);

        return FinanceHistoryCalculator.Summarize(session.Entries, config, teamId)
            .Where(operation => operation.Direction == FinanceHistoryCalculator.MoneyDirection.Expense)
            .GroupBy(operation => operation.Type)
            .ToDictionary(group => group.Key, group => group.Sum(operation => operation.Amount));
    }
}
