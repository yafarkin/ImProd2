using Game.Config;
using Game.Config.Catalog;
using Game.Config.Economy;
using Game.Config.Loading;
using Game.Config.Session;

namespace Game.Balancing.Tests;

/// <summary>
/// <see cref="ChainDesignRules"/> — §0 диагностики: четыре правила дизайна, каждое из которых было
/// нарушено на живом файле и стоило отдельного расследования
/// (<c>docs/production-chain-calibration-lessons.md</c>). Проверка валидируется тем же способом, что
/// и §1d: сначала на синтетике, где нарушение внесено намеренно и его величина известна точно, и
/// только потом применяется к боевым файлам.
///
/// <para>
/// Обе боевые цепочки обязаны быть чистыми — это одновременно и регрессия на них самих, и
/// доказательство, что правила не срабатывают вхолостую на нормальном контенте.
/// </para>
/// </summary>
public class ChainDesignRulesTests
{
    private static readonly SyntheticChainConfigBuilder.LevelParams[] HealthyLevels =
    [
        new(BuildCost: 350m, FixedCostPerTurn: 6.67m, ProductionRate: 50m),
        new(BuildCost: 800m, FixedCostPerTurn: 20m, ProductionRate: 20m),
        new(BuildCost: 1650m, FixedCostPerTurn: 46.67m, ProductionRate: 7.5m),
    ];

    [Theory]
    [InlineData("training-1-sector.json")]
    [InlineData("main-3-sectors.json")]
    public void Shipped_Chains_Break_No_Design_Rule(string productionModelFileName)
    {
        var config = GameConfigLoader.LoadFromFiles(
            Path.Combine(AppContext.BaseDirectory, "Samples", "production-models", productionModelFileName),
            Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));

        var findings = ChainDesignRules.Check(config);

        Assert.True(
            findings.Count == 0,
            $"{productionModelFileName}: " + string.Join(" | ", findings.Select(f => $"[{f.Rule}] {f.Message}")));
    }

    [Fact]
    public void Healthy_Synthetic_Chain_Breaks_No_Rule()
    {
        var config = SyntheticChainConfigBuilder.Build(HealthyLevels, inputQuantityPerLevel: 2m);

        Assert.Empty(ChainDesignRules.Check(config));
    }

    /// <summary>
    /// Капзатраты выше потолка минуса: цепочка стоит 350 + 800 + 1650 = 2800, потолок ставим 1000 —
    /// команда физически не достроит верхний уровень.
    /// </summary>
    [Fact]
    public void Total_Build_Cost_Above_The_Negative_Balance_Ceiling_Is_Reported_With_The_Required_Ceiling()
    {
        var raw = SyntheticChainConfigBuilder.Build(HealthyLevels, inputQuantityPerLevel: 2m).Raw;
        var config = GameConfigLoader.Load(raw with
        {
            StartingConditions = new StartingConditionsConfig { MaxInitialBuildBudget = 1000m },
        });

        var finding = Assert.Single(ChainDesignRules.Check(config), f => f.Rule == "капзатраты выше потолка минуса");
        Assert.Contains("2800", finding.Message);
        Assert.Contains("1000", finding.Message);
    }

    /// <summary>
    /// Оборот выше бесплатного склада: сырьевой уровень один даёт 500 ед./ход, лимит ставим 100 —
    /// сбор за превышение начнёт съедать цепочку по причине, не связанной с производством.
    /// </summary>
    [Fact]
    public void Throughput_Above_The_Free_Warehouse_Limit_Is_Reported()
    {
        var raw = SyntheticChainConfigBuilder.Build(HealthyLevels, inputQuantityPerLevel: 2m).Raw;
        var config = GameConfigLoader.Load(raw with
        {
            Warehouse = raw.Warehouse with { FreeCapacity = 100m },
        });

        var finding = Assert.Single(ChainDesignRules.Check(config), f => f.Rule == "оборот выше бесплатного склада");
        Assert.Contains("100", finding.Message);
    }

    /// <summary>
    /// Поздняя разблокировка: у синтетического построителя всё открыто с первого хода
    /// (<c>StartingGeneration</c> = число уровней), поэтому нарушение вносим вручную — стартовое
    /// поколение 1 и порог, недостижимый за отведённые ходы.
    /// </summary>
    [Fact]
    public void Last_Level_Unlocked_Too_Late_Is_Reported_With_The_Unlock_Turn()
    {
        var raw = SyntheticChainConfigBuilder.Build(HealthyLevels, inputQuantityPerLevel: 2m).Raw;
        var config = GameConfigLoader.Load(raw with
        {
            Duration = new SessionDurationConfig { MinTurns = 40, MaxTurns = 40 },
            GenerationResearch = raw.GenerationResearch with
            {
                StartingGeneration = 1,
                // Порог i открывает поколение StartingGeneration+i+1, то есть единственный порог
                // здесь открывает верхний уровень 2. Очки = √вложений при потолке 300/ход: нужно
                // 100² = 10 000, то есть ход 34 из 40 — много позже четверти партии (ход 10).
                ResearchPointThresholdsByGeneration = [100m],
                DiminishingReturnsExponent = 0.5m,
                MaxCommitmentPerTurn = 300m,
            },
        });

        var finding = Assert.Single(ChainDesignRules.Check(config), f => f.Rule == "поздняя разблокировка");
        Assert.Contains("на ходу 34", finding.Message);
        Assert.Contains("ход 10", finding.Message);
    }

    /// <summary>
    /// Аддитивный кросс-вход — та самая ошибка, что молча удваивала стоимость сырья. Строим два
    /// сектора по три материала на уровне 1: у двух рецептов вход 1.0 своего сырья, у третьего —
    /// 1.0 своего ПЛЮС 1.0 импортного вместо деления пополам.
    /// </summary>
    [Fact]
    public void Additive_Cross_Sector_Input_Is_Flagged_Against_The_Level_Median()
    {
        var config = BuildTwoSectorLevelOneConfig(additiveCrossInput: true);

        var finding = Assert.Single(ChainDesignRules.Check(config), f => f.Rule == "аддитивный кросс-вход");
        Assert.Contains("b-plus", finding.Message);
        Assert.Contains("ДЕЛИТЬ", finding.Message);
    }

    /// <summary>Тот же граф, но кросс-вход делит объём со своим сырьём — тревоги быть не должно, иначе правило ловило бы саму кросс-связь, а не её аддитивность.</summary>
    [Fact]
    public void Cross_Sector_Input_That_Splits_The_Quantity_Is_Not_Flagged()
    {
        var config = BuildTwoSectorLevelOneConfig(additiveCrossInput: false);

        Assert.DoesNotContain(ChainDesignRules.Check(config), f => f.Rule == "аддитивный кросс-вход");
    }

    /// <summary>
    /// Два сектора, по два сырьевых материала и по паре переделов уровня 1, плюс третий передел,
    /// который и несёт проверяемый кросс-вход. Трёх рецептов на уровне достаточно, чтобы медиана была
    /// осмысленной (правило требует минимум три — на двух «медиана» ничего не значит).
    /// </summary>
    private static ResolvedGameConfig BuildTwoSectorLevelOneConfig(bool additiveCrossInput)
    {
        var raw = SyntheticChainConfigBuilder.Build(HealthyLevels, inputQuantityPerLevel: 2m).Raw;

        var ownQuantity = additiveCrossInput ? 1.0m : 0.5m;
        var materials = new List<MaterialConfig>
        {
            new() { Id = "a-raw", Name = "Сырьё А", SectorId = "A", Level = 0 },
            new() { Id = "b-raw", Name = "Сырьё Б", SectorId = "B", Level = 0 },
            new() { Id = "a-one", Name = "Передел А1", SectorId = "A", Level = 1 },
            new() { Id = "a-two", Name = "Передел А2", SectorId = "A", Level = 1 },
            new() { Id = "b-plus", Name = "Передел Б с импортом", SectorId = "B", Level = 1 },
        };

        var recipes = new List<RecipeConfig>
        {
            new() { Id = "a-mining", OutputMaterialId = "a-raw", OutputQuantity = 1m, Inputs = [], ProductionRate = 50m },
            new() { Id = "b-mining", OutputMaterialId = "b-raw", OutputQuantity = 1m, Inputs = [], ProductionRate = 50m },
            new() { Id = "a-one-mill", OutputMaterialId = "a-one", OutputQuantity = 1m, ProductionRate = 20m, Inputs = [new RecipeInputConfig { MaterialId = "a-raw", Quantity = 1m }] },
            new() { Id = "a-two-mill", OutputMaterialId = "a-two", OutputQuantity = 1m, ProductionRate = 20m, Inputs = [new RecipeInputConfig { MaterialId = "a-raw", Quantity = 1m }] },
            new()
            {
                Id = "b-plus-mill", OutputMaterialId = "b-plus", OutputQuantity = 1m, ProductionRate = 20m,
                Inputs =
                [
                    new RecipeInputConfig { MaterialId = "b-raw", Quantity = ownQuantity },
                    new RecipeInputConfig { MaterialId = "a-raw", Quantity = additiveCrossInput ? 1.0m : 0.5m },
                ],
            },
        };

        var factories = new List<FactoryDefinitionConfig>
        {
            new() { Id = "a-mine", Name = "Рудник А", SectorId = "A", RecipeIds = ["a-mining"], BuildCost = 350m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 7m },
            new() { Id = "b-mine", Name = "Рудник Б", SectorId = "B", RecipeIds = ["b-mining"], BuildCost = 350m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 7m },
            new() { Id = "a-one-plant", Name = "Завод А1", SectorId = "A", RecipeIds = ["a-one-mill"], BuildCost = 800m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 20m },
            new() { Id = "a-two-plant", Name = "Завод А2", SectorId = "A", RecipeIds = ["a-two-mill"], BuildCost = 800m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 20m },
            new() { Id = "b-plus-plant", Name = "Завод Б+", SectorId = "B", RecipeIds = ["b-plus-mill"], BuildCost = 800m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 20m },
        };

        return GameConfigLoader.Load(raw with
        {
            Sectors = [new SectorConfig { Id = "A", Name = "Сектор А" }, new SectorConfig { Id = "B", Name = "Сектор Б" }],
            Materials = materials,
            Recipes = recipes,
            FactoryDefinitions = factories,
            GenerationResearch = raw.GenerationResearch with { StartingGeneration = 1 },
            Economy = raw.Economy with
            {
                BaseMarketPerMaterial = materials
                    .Select(m => new MaterialMarketConfig { MaterialId = m.Id, BasePrice = 10m, BaseCapacity = 1_000_000m })
                    .ToList(),
            },
        });
    }
}
