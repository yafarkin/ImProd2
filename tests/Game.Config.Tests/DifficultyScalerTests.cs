using Game.Config.Catalog;
using Game.Config.Economy;

namespace Game.Config.Tests;

/// <summary>
/// Интерполяция и применение шести рычагов сложности (<c>docs/difficulty.md</c>, было восемь: рычаг
/// ставки по займу убран вместе с банковским займом как классом механики (docs/TODO.md #23), рычаг
/// эскалации командной зарплаты убран вместе с самой прогрессией (rebalance/2-sector-stepwise,
/// 2026-08-23)) — <see cref="DifficultyScaler"/>, шаг 2 плана реализации из этого документа.
/// </summary>
public class DifficultyScalerTests
{
    private static GameConfig BuildConfig()
    {
        var config = GameConfigTestBuilder.Build(
            factoryDefinitions: new[]
            {
                new FactoryDefinitionConfig
                {
                    Id = "mine", Name = "Рудник", SectorId = "A", RecipeIds = Array.Empty<string>(),
                    // FixedCostPerTurn обязан быть ненулевым: с 2026-09-07 это один из шести рычагов бегунка
                    // сложности (заменил мёртвый BaseSellPrice), а множитель на нуле неотличим от отсутствия рычага.
                    BuildCost = 1000m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 50m,
                },
            });

        return config with
        {
            Economy = config.Economy with
            {
                BaseMarketPerMaterial = new[] { new MaterialMarketConfig { MaterialId = "ore", BaseSellPrice = 10m, BaseCapacity = 100m } },
            },
            GenerationResearch = config.GenerationResearch with
            {
                ResearchPointThresholdsByGeneration = new[] { 500m },
            },
        };
    }

    /// <summary>
    /// Тот же конфиг, но на экзогенной цене — с блока 11.11 половина рычагов зависит от модели
    /// ценообразования (<c>docs/difficulty.md</c> §9), поэтому обе ветки проверяются отдельно.
    /// </summary>
    private static GameConfig BuildExternalConfig()
    {
        var config = BuildConfig();
        return config with { Economy = config.Economy with { PricingModel = PricingModel.External } };
    }

    [Theory]
    [InlineData(PricingModel.CostPlus)]
    [InlineData(PricingModel.External)]
    public void Apply_At_Level_Three_Leaves_The_Config_Unchanged(PricingModel pricingModel)
    {
        var config = pricingModel == PricingModel.External ? BuildExternalConfig() : BuildConfig();

        var scaled = DifficultyScaler.Apply(config, 3.0);

        Assert.Equivalent(config, scaled, strict: true);
    }

    [Fact]
    public void Apply_Interpolates_Linearly_Between_The_First_Two_Anchors()
    {
        var config = BuildConfig();

        // BuildCost-анкеры уровней 0/1 — 0.5/0.7 (docs/difficulty.md §3), на уровне 0.5 — ровно
        // среднее, 0.6.
        var scaled = DifficultyScaler.Apply(config, 0.5);

        Assert.Equal(600m, scaled.FactoryDefinitions.Single().BuildCost, precision: 6);
    }

    [Fact]
    public void Apply_Interpolates_Linearly_Between_The_Last_Two_Anchors()
    {
        var config = BuildConfig();

        // BuildCost-анкеры уровней 4/5 под cost-plus — 1.08/1.15 (пересчёт 2026-09-07,
        // docs/difficulty.md §8), на уровне 4.7 (вес 0.7 к пятому) — 1.08 + 0.07*0.7 = 1.129.
        // Под экзогенной ценой тяжёлая сторона этого рычага плоская, см. отдельный тест ниже.
        var scaled = DifficultyScaler.Apply(config, 4.7);

        Assert.Equal(1129m, scaled.FactoryDefinitions.Single().BuildCost, precision: 3);
    }

    [Fact]
    public void Apply_Clamps_Levels_Below_Zero_To_The_Zero_Anchor()
    {
        var config = BuildConfig();

        var atMinusOne = DifficultyScaler.Apply(config, -1.0);
        var atZero = DifficultyScaler.Apply(config, 0.0);

        Assert.Equal(atZero.FactoryDefinitions.Single().BuildCost, atMinusOne.FactoryDefinitions.Single().BuildCost);
    }

    [Fact]
    public void Apply_Clamps_Levels_Above_Five_To_The_Five_Anchor()
    {
        var config = BuildConfig();

        var atSeven = DifficultyScaler.Apply(config, 7.0);
        var atFive = DifficultyScaler.Apply(config, 5.0);

        Assert.Equal(atFive.FactoryDefinitions.Single().BuildCost, atSeven.FactoryDefinitions.Single().BuildCost);
    }

    /// <summary>
    /// На лёгкой стороне двигаются только ПЯТЬ рычагов из шести: наценка аварийной закупки
    /// (<see cref="EmergencyPurchaseBaseMultiplierAnchors"/> в коде) приколочена к 1.0 на уровнях 0–3
    /// намеренно (пересчёт 2026-09-07, docs/difficulty.md §8) — опустить её ниже потолка жадности бота
    /// (+50%) значит сломать «бутерброд наценок» §2 диагностики, а дефолт +55% уже почти вплотную к
    /// этому потолку. Это не мёртвый рычаг, как когда-то `BaseSellPrice`, а рычаг с занятой инвариантом
    /// лёгкой стороной: тяжёлая сторона (уровни 4–5) по-прежнему работает, см. тест ниже.
    /// </summary>
    [Fact]
    public void Apply_At_Level_Zero_Moves_The_Five_Unpinned_Levers_In_The_Easier_Direction()
    {
        var config = BuildConfig();

        var scaled = DifficultyScaler.Apply(config, 0.0);

        Assert.True(scaled.FactoryDefinitions.Single().BuildCost < config.FactoryDefinitions.Single().BuildCost);
        Assert.True(scaled.Rnd.ProductionRateBonusPerLevel > config.Rnd.ProductionRateBonusPerLevel);
        Assert.True(scaled.Rnd.ResearchPointThresholdsByLevel[0] < config.Rnd.ResearchPointThresholdsByLevel[0]);
        Assert.True(scaled.GenerationResearch.ResearchPointThresholdsByGeneration[0] < config.GenerationResearch.ResearchPointThresholdsByGeneration[0]);
        Assert.True(scaled.FactoryDefinitions.Single().FixedCostPerTurn > config.FactoryDefinitions.Single().FixedCostPerTurn);
        Assert.True(scaled.Wear.AccelerationFactorPerTurn < config.Wear.AccelerationFactorPerTurn);

        // Приколочена — не двигается на лёгкой стороне вовсе.
        Assert.Equal(config.Economy.EmergencyPurchaseBaseMultiplier, scaled.Economy.EmergencyPurchaseBaseMultiplier);
    }

    [Fact]
    public void Apply_At_Level_Five_Moves_All_Six_Levers_In_The_Harder_Direction()
    {
        var config = BuildConfig();

        var scaled = DifficultyScaler.Apply(config, 5.0);

        Assert.True(scaled.FactoryDefinitions.Single().BuildCost > config.FactoryDefinitions.Single().BuildCost);
        Assert.True(scaled.Rnd.ProductionRateBonusPerLevel < config.Rnd.ProductionRateBonusPerLevel);
        Assert.True(scaled.Rnd.ResearchPointThresholdsByLevel[0] > config.Rnd.ResearchPointThresholdsByLevel[0]);
        Assert.True(scaled.GenerationResearch.ResearchPointThresholdsByGeneration[0] > config.GenerationResearch.ResearchPointThresholdsByGeneration[0]);
        Assert.True(scaled.FactoryDefinitions.Single().FixedCostPerTurn < config.FactoryDefinitions.Single().FixedCostPerTurn);
        Assert.True(scaled.Economy.EmergencyPurchaseBaseMultiplier > config.Economy.EmergencyPurchaseBaseMultiplier);
        Assert.True(scaled.Wear.AccelerationFactorPerTurn > config.Wear.AccelerationFactorPerTurn);
    }

    /// <summary>
    /// Рычаг доходности берётся по модели ценообразования (блок 11.11, <c>docs/difficulty.md</c> §9):
    /// под <see cref="PricingModel.External"/> задатчик прибыли — цена, и бегунок двигает
    /// <c>BaseSellPrice</c>; под <see cref="PricingModel.CostPlus"/> цена не участвует ни в одной
    /// денежной операции (и системная продажа, и аварийная закупка берут её из
    /// <c>MaterialCostCalculator</c>), и трогать её значило бы завысить измеренную сложность на
    /// несуществующий эффект.
    /// </summary>
    [Fact]
    public void The_Profitability_Lever_Is_The_Sell_Price_Under_External_Pricing()
    {
        var config = BuildExternalConfig();
        var basePrice = config.Economy.BaseMarketPerMaterial.Single().BaseSellPrice;

        var easy = DifficultyScaler.Apply(config, 0.0);
        var hard = DifficultyScaler.Apply(config, 5.0);

        Assert.True(easy.Economy.BaseMarketPerMaterial.Single().BaseSellPrice > basePrice);
        Assert.True(hard.Economy.BaseMarketPerMaterial.Single().BaseSellPrice < basePrice);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(5.0)]
    public void The_Sell_Price_Lever_Stays_Silent_Under_Cost_Plus(double difficultyLevel)
    {
        var config = BuildConfig();

        var scaled = DifficultyScaler.Apply(config, difficultyLevel);

        Assert.Equal(
            config.Economy.BaseMarketPerMaterial.Single().BaseSellPrice,
            scaled.Economy.BaseMarketPerMaterial.Single().BaseSellPrice);
    }

    /// <summary>
    /// Зеркальная половина того же правила: содержание фабрики — задатчик прибыли только под
    /// cost-plus. Под экзогенной ценой его рост был бы чистым убытком, то есть двигал бы сложность в
    /// ту же сторону, что и цена, — рычаг применялся бы дважды.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(5.0)]
    public void The_Upkeep_Lever_Stays_Silent_Under_External_Pricing(double difficultyLevel)
    {
        var config = BuildExternalConfig();

        var scaled = DifficultyScaler.Apply(config, difficultyLevel);

        Assert.Equal(
            config.FactoryDefinitions.Single().FixedCostPerTurn,
            scaled.FactoryDefinitions.Single().FixedCostPerTurn);
    }

    /// <summary>
    /// Под экзогенной ценой тяжёлая сторона капитальных затрат приколочена к 1.0: запас окупаемости
    /// §1b — общий бюджет тяжёлой стороны, и он весь отдан рычагу цены, который вчетверо сильнее.
    /// Лёгкая сторона у обеих моделей общая.
    /// </summary>
    [Fact]
    public void Build_Cost_Rises_With_Difficulty_Only_Under_Cost_Plus()
    {
        var costPlus = BuildConfig();
        var external = BuildExternalConfig();

        Assert.True(DifficultyScaler.Apply(costPlus, 5.0).FactoryDefinitions.Single().BuildCost
                    > costPlus.FactoryDefinitions.Single().BuildCost);
        Assert.Equal(
            external.FactoryDefinitions.Single().BuildCost,
            DifficultyScaler.Apply(external, 5.0).FactoryDefinitions.Single().BuildCost);
        Assert.Equal(
            DifficultyScaler.Apply(costPlus, 0.0).FactoryDefinitions.Single().BuildCost,
            DifficultyScaler.Apply(external, 0.0).FactoryDefinitions.Single().BuildCost);
    }

    /// <summary>
    /// Бегунок обязан оставаться монотонным по итоговой доходности в обеих моделях — это то
    /// свойство, ради которого рычаг вообще выбирается по модели: таблица cost-plus под экзогенной
    /// ценой работала бы В ОБРАТНУЮ сторону (рост содержания на лёгком краю — чистый убыток).
    /// </summary>
    [Fact]
    public void The_Profitability_Lever_Is_Monotone_Across_All_Six_Levels_In_Both_Models()
    {
        var external = Enumerable.Range(0, 6)
            .Select(level => DifficultyScaler.Apply(BuildExternalConfig(), level).Economy.BaseMarketPerMaterial.Single().BaseSellPrice)
            .ToList();
        var costPlus = Enumerable.Range(0, 6)
            .Select(level => DifficultyScaler.Apply(BuildConfig(), level).FactoryDefinitions.Single().FixedCostPerTurn)
            .ToList();

        // Дороже продавать = легче; дороже содержать фабрику под cost-plus = тоже легче (прибыль там
        // равна 0.30 × издержки), поэтому обе последовательности убывают с ростом сложности.
        Assert.Equal(external.OrderByDescending(price => price), external);
        Assert.Equal(costPlus.OrderByDescending(cost => cost), costPlus);
    }
}
