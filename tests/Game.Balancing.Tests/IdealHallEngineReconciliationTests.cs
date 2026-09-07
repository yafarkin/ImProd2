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
/// <b>Что специально выведено за скобки.</b> Капремонт — зал не моделирует износ вовсе
/// (<c>docs/TODO.md</c> №18), поэтому его в разбивке зала нет по построению. Аварийная закупка — зал
/// по построению никогда не остаётся без сырья. Доходы (продажа системе) не сверяются: зал продаёт
/// излишек каждый ход, а сценарий на движке ничего не продаёт, чтобы не тащить в проверку ещё и
/// поведение бота. Это осознанное ограничение: все три исторических дефекта были в РАСХОДАХ, и
/// именно расходы здесь и сверяются.
/// </para>
/// </summary>
public class IdealHallEngineReconciliationTests
{
    private const int HallTurns = 8;

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

    private static IReadOnlyDictionary<FinanceHistoryCalculator.OperationType, decimal> RunScriptedEngineScenario(
        ResolvedGameConfig config) => RunScriptedEngineScenario(config, out _, out _, out _, out _);

    private static IReadOnlyDictionary<FinanceHistoryCalculator.OperationType, decimal> RunScriptedEngineScenario(
        ResolvedGameConfig config, out decimal producedUnits, out decimal finalElectricityPrice) =>
        RunScriptedEngineScenario(config, out producedUnits, out finalElectricityPrice, out _, out _);

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
        out decimal startingCash)
    {
        const decimal grant = 10_000_000m;
        var sector = config.Sectors.Single();
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config, endTurn: HallTurns + 1, [new TeamSpec { Id = teamId, Name = "Сценарий", SectorId = sector.Id }]);
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

            session.AdvancePhase(PhaseTransitionTrigger.Timer);
        }

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
