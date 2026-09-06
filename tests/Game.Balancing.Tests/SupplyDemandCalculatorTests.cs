using Game.Config.Catalog;
using Game.Config.Economy;
using Game.Config.Loading;
using Game.Engine;

namespace Game.Balancing.Tests;

/// <summary>
/// <see cref="SupplyDemandCalculator"/> — §1d диагностики (в плане исследований
/// <c>docs/rebalance-2sector/balance-experiment-plan.md</c> «итерация 2, §5»). Проверка валидируется
/// сначала на синтетике, и только потом применяется к боевым файлам (правило того же плана): здоровая
/// цепочка обязана быть чистой, заведомо больная — пойматься с точностью до посчитанного вручную
/// процента, а не «примерно».
/// </summary>
public class SupplyDemandCalculatorTests
{
    /// <summary>Те же числа, что у <c>IdealHallUpperBoundTests</c> — заведомо здоровая цепочка из реального фикса направления B.</summary>
    private static readonly SyntheticChainConfigBuilder.LevelParams[] HealthyLevels =
    [
        new(BuildCost: 350m, FixedCostPerTurn: 6.67m, ProductionRate: 50m),
        new(BuildCost: 800m, FixedCostPerTurn: 20m, ProductionRate: 20m),
        new(BuildCost: 1650m, FixedCostPerTurn: 46.67m, ProductionRate: 7.5m),
    ];

    [Fact]
    public void Healthy_Chain_Has_No_Deficits()
    {
        // Вход — 2 единицы предыдущего уровня на 1 единицу выпуска, 10 рабочих на фабрику:
        // уровень 0 выпускает 500/ход, уровень 1 просит 2×200 = 400 => 1.25×;
        // уровень 1 выпускает 200/ход, уровень 2 просит 2×75 = 150 => 1.33×.
        var config = SyntheticChainConfigBuilder.Build(HealthyLevels, inputQuantityPerLevel: 2m);

        var balances = SupplyDemandCalculator.Calculate(config, workersPerFactory: 10);

        Assert.Empty(SupplyDemandCalculator.FormatDeficits(balances, ChainCapacityPlanner.Plan(config)));
        Assert.Empty(SupplyDemandCalculator.FormatThinSlack(balances));
        Assert.Equal(1.25m, balances.Single(b => b.Level == 0).Ratio);
        Assert.Equal(200m / 150m, balances.Single(b => b.Level == 1).Ratio);
    }

    [Fact]
    public void Halving_A_Producer_ProductionRate_Is_Caught_With_The_Exact_Deficit()
    {
        // Ровно тот же конфиг, но сырьевой уровень режем вдвое: выпуск 250/ход против прежнего
        // спроса 400/ход => 0.625× (нехватка 37.5%). Уровень 1 при этом остаётся здоровым — проверка
        // обязана указать ровно на сломанный материал, а не разлиться по всей цепочке.
        var brokenLevels = HealthyLevels.ToArray();
        brokenLevels[0] = brokenLevels[0] with { ProductionRate = 25m };
        var config = SyntheticChainConfigBuilder.Build(brokenLevels, inputQuantityPerLevel: 2m);

        var balances = SupplyDemandCalculator.Calculate(config, workersPerFactory: 10);

        var broken = balances.Single(b => b.Level == 0);
        Assert.Equal(250m, broken.SupplyPerTurn);
        Assert.Equal(400m, broken.DemandPerTurn);
        Assert.Equal(0.625m, broken.Ratio);
        Assert.True(broken.IsDeficit);
        Assert.False(balances.Single(b => b.Level == 1).IsDeficit);

        // Разрыв 0.625× закрывается доньмом (см. ChainCapacityPlannerTests) — значит для вердикта это
        // не поломка, а решение игрока: FormatDeficits молчит, а план расширения показывает цену вопроса.
        var capacityPlan = ChainCapacityPlanner.Plan(config);
        Assert.Empty(SupplyDemandCalculator.FormatDeficits(balances, capacityPlan));
        var expansion = SupplyDemandCalculator.FormatPlannedExpansion(capacityPlan, baseWorkerCount: 10);
        Assert.Single(expansion);
        Assert.Contains("рабочих", expansion[0]);
    }

    /// <summary>
    /// Ловушка, на которой этот же расчёт вручную ошибался дважды (<c>fastener-plant</c> 2026-08-23,
    /// <c>wire-rod</c> 2026-08-24): рецепт «1 пруток → 20 крепежей» потребляет не 20 прутков на партию,
    /// а 1 — делить на <c>OutputQuantity</c> потребителя обязательно. Без деления этот конфиг показал
    /// бы ложный дефицит 0.05×.
    /// </summary>
    [Fact]
    public void Consumer_With_OutputQuantity_Above_One_Does_Not_Get_A_False_Deficit()
    {
        var config = BuildOneToManyConfig(outputQuantityOfConsumer: 20m);

        var balances = SupplyDemandCalculator.Calculate(config, workersPerFactory: 10);

        var input = balances.Single(b => b.MaterialId == "bar");
        // Потребитель делает 20 крепежей за партию из 1 прутка: выпуск 2000 крепежей = 100 партий = 100 прутков.
        Assert.Equal(100m, input.DemandPerTurn);
        Assert.Equal(500m, input.SupplyPerTurn);
        Assert.False(input.IsDeficit);
        Assert.Empty(SupplyDemandCalculator.FormatDeficits(balances, ChainCapacityPlanner.Plan(config)));
    }

    /// <summary>Конечный продукт никто не потребляет — это не дефицит и не запас, балансировать нечего.</summary>
    [Fact]
    public void Material_Without_Consumers_Has_No_Ratio_And_Is_Not_Reported()
    {
        var config = SyntheticChainConfigBuilder.Build(HealthyLevels, inputQuantityPerLevel: 2m);

        var balances = SupplyDemandCalculator.Calculate(config, workersPerFactory: 10);

        var finalProduct = balances.Single(b => b.Level == 2);
        Assert.Null(finalProduct.Ratio);
        Assert.False(finalProduct.IsDeficit);
        Assert.False(finalProduct.IsThinSlack);
    }

    [Fact]
    public void Thin_Slack_Is_Reported_Separately_From_A_Real_Deficit()
    {
        // Выпуск сырья 410/ход против спроса 400/ход = 1.025× — дефицита нет, запас тоньше порога.
        var thinLevels = HealthyLevels.ToArray();
        thinLevels[0] = thinLevels[0] with { ProductionRate = 41m };
        var config = SyntheticChainConfigBuilder.Build(thinLevels, inputQuantityPerLevel: 2m);

        var balances = SupplyDemandCalculator.Calculate(config, workersPerFactory: 10);

        Assert.Empty(SupplyDemandCalculator.FormatDeficits(balances, ChainCapacityPlanner.Plan(config)));
        Assert.Single(SupplyDemandCalculator.FormatThinSlack(balances));
    }

    /// <summary>
    /// Разрыв, который рычагами не закрыть (планировщик упёрся в оба своих потолка), обязан остаться
    /// блокирующим — иначе §1d перестала бы ловить что-либо вообще.
    /// </summary>
    [Fact]
    public void Deficit_That_Levers_Cannot_Close_Is_Still_Reported()
    {
        // Сырьё в 40 раз слабее спроса: 4 фабрики по 30 рабочих его не закрывают.
        var brokenLevels = HealthyLevels.ToArray();
        brokenLevels[0] = brokenLevels[0] with { ProductionRate = 1m };
        var config = SyntheticChainConfigBuilder.Build(brokenLevels, inputQuantityPerLevel: 2m);

        var balances = SupplyDemandCalculator.Calculate(config, workersPerFactory: 10);
        var deficits = SupplyDemandCalculator.FormatDeficits(balances, ChainCapacityPlanner.Plan(config));

        Assert.Single(deficits);
        Assert.Contains("НЕ закрывается", deficits[0]);
    }

    /// <summary>Один сырьевой уровень и один потребитель, у которого <c>OutputQuantity</c> больше единицы.</summary>
    private static ResolvedGameConfig BuildOneToManyConfig(decimal outputQuantityOfConsumer)
    {
        var baseConfig = SyntheticChainConfigBuilder.Build(HealthyLevels, inputQuantityPerLevel: 2m).Raw;
        var config = baseConfig with
        {
            Materials =
            [
                new MaterialConfig { Id = "bar", Name = "Пруток", SectorId = baseConfig.Sectors[0].Id, Level = 0 },
                new MaterialConfig { Id = "fastener", Name = "Крепёж", SectorId = baseConfig.Sectors[0].Id, Level = 1 },
            ],
            Recipes =
            [
                new RecipeConfig { Id = "bar-mill", OutputMaterialId = "bar", OutputQuantity = 1m, Inputs = [], ProductionRate = 50m },
                new RecipeConfig
                {
                    Id = "fastener-plant", OutputMaterialId = "fastener", OutputQuantity = outputQuantityOfConsumer,
                    Inputs = [new RecipeInputConfig { MaterialId = "bar", Quantity = 1m }], ProductionRate = 200m,
                },
            ],
            FactoryDefinitions =
            [
                new FactoryDefinitionConfig
                {
                    Id = "bar-plant", Name = "Прокат", SectorId = baseConfig.Sectors[0].Id, RecipeIds = ["bar-mill"],
                    BuildCost = 350m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 6.67m,
                },
                new FactoryDefinitionConfig
                {
                    Id = "fastener-shop", Name = "Крепёж", SectorId = baseConfig.Sectors[0].Id, RecipeIds = ["fastener-plant"],
                    BuildCost = 800m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 20m,
                },
            ],
            Economy = baseConfig.Economy with
            {
                BaseMarketPerMaterial =
                [
                    new MaterialMarketConfig { MaterialId = "bar", BasePrice = 1m, BaseCapacity = 1000m },
                    new MaterialMarketConfig { MaterialId = "fastener", BasePrice = 1m, BaseCapacity = 1000m },
                ],
            },
        };

        return GameConfigLoader.Load(config);
    }
}
