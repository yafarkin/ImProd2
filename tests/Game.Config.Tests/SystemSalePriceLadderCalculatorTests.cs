using Game.Config.Catalog;
using Game.Config.Economy;
using Game.Config.Loading;

namespace Game.Config.Tests;

/// <summary>
/// Лестница экзогенных цен сбыта (блок 11.2, <c>docs/external-economy.md</c> §4):
/// <c>цена = себестоимость × (1 + BaseMargin + DepthBonus × уровень)</c>.
///
/// <para>Себестоимость подаётся тестами готовым словарём, а не считается
/// <c>MaterialCostCalculator</c>: лестница обязана быть чистой функцией от (конфиг, себестоимость,
/// две ручки), и проверять её надо в отрыве от того, кто именно посчитал себестоимость.</para>
/// </summary>
public class SystemSalePriceLadderCalculatorTests
{
    // rock (A, ур.0) → iron (A, ур.1) → iron-sheet (A, ур.2); отдельно oil (B, ур.0) → plastic (B, ур.1).
    private static ResolvedGameConfig BuildConfig()
    {
        var raw = GameConfigTestBuilder.Build(
            sectors: new[]
            {
                new SectorConfig { Id = "A", Name = "Металлургия" },
                new SectorConfig { Id = "B", Name = "Нефтехимия" },
            },
            materials: new[]
            {
                new MaterialConfig { Id = "rock", Name = "Порода", SectorId = "A", Level = 0 },
                new MaterialConfig { Id = "iron", Name = "Железо", SectorId = "A", Level = 1 },
                new MaterialConfig { Id = "iron-sheet", Name = "Листы", SectorId = "A", Level = 2 },
                new MaterialConfig { Id = "oil", Name = "Нефть", SectorId = "B", Level = 0 },
                new MaterialConfig { Id = "plastic", Name = "Пластик", SectorId = "B", Level = 1 },
            },
            recipes: new[]
            {
                new RecipeConfig { Id = "rock-mining", OutputMaterialId = "rock", OutputQuantity = 1, Inputs = Array.Empty<RecipeInputConfig>(), ProductionRate = 1000 },
                new RecipeConfig { Id = "iron-extraction", OutputMaterialId = "iron", OutputQuantity = 1, Inputs = new[] { new RecipeInputConfig { MaterialId = "rock", Quantity = 10 } }, ProductionRate = 100 },
                new RecipeConfig { Id = "iron-sheet-from-iron", OutputMaterialId = "iron-sheet", OutputQuantity = 1, Inputs = new[] { new RecipeInputConfig { MaterialId = "iron", Quantity = 10 } }, ProductionRate = 10 },
                new RecipeConfig { Id = "oil-drilling", OutputMaterialId = "oil", OutputQuantity = 1, Inputs = Array.Empty<RecipeInputConfig>(), ProductionRate = 1 },
                new RecipeConfig { Id = "plastic-from-oil", OutputMaterialId = "plastic", OutputQuantity = 1, Inputs = new[] { new RecipeInputConfig { MaterialId = "oil", Quantity = 2 } }, ProductionRate = 1 },
            });

        raw = raw with
        {
            Economy = raw.Economy with
            {
                BaseMarketPerMaterial = new[]
                {
                    new MaterialMarketConfig { MaterialId = "rock", BaseSellPrice = 0.02m, BaseCapacity = 5000m },
                    new MaterialMarketConfig { MaterialId = "iron", BaseSellPrice = 15m, BaseCapacity = 500m },
                    new MaterialMarketConfig { MaterialId = "iron-sheet", BaseSellPrice = 40m, BaseCapacity = 50m },
                    new MaterialMarketConfig { MaterialId = "oil", BaseSellPrice = 12m, BaseCapacity = 150m },
                    new MaterialMarketConfig { MaterialId = "plastic", BaseSellPrice = 28m, BaseCapacity = 100m },
                },
            },
        };

        return GameConfigLoader.Load(GameConfigWriter.Save(raw));
    }

    private static Dictionary<string, decimal> Costs() => new()
    {
        ["rock"] = 1m,
        ["iron"] = 10m,
        ["iron-sheet"] = 100m,
        ["oil"] = 2m,
        ["plastic"] = 20m,
    };

    /// <summary>
    /// Опорное свойство всего перехода на экзогенную цену: при нулевой надбавке за глубину лестница
    /// в точности воспроизводит прежнее правило cost-plus (одна наценка на все уровни). Благодаря
    /// этому перекалибровка (блок 11.8) стартует не с нуля, а из заведомо проходимой точки, и
    /// сводится к подъёму одной ручки.
    /// </summary>
    [Fact]
    public void With_No_Depth_Bonus_The_Ladder_Reproduces_Plain_Cost_Plus_Pricing()
    {
        var rows = SystemSalePriceLadderCalculator.Calculate(BuildConfig(), Costs(), baseMargin: 0.30m, depthBonusPerLevel: 0m);

        Assert.All(rows, row => Assert.Equal(row.UnitCost * 1.30m, row.NewPrice));
        Assert.All(rows, row => Assert.Equal(0.30m, row.MarginRate, precision: 10));
    }

    /// <summary>Надбавка за глубину добавляется к наценке ровно по одному разу на каждый уровень передела.</summary>
    [Fact]
    public void The_Depth_Bonus_Adds_One_Step_Of_Margin_Per_Processing_Level()
    {
        var rows = SystemSalePriceLadderCalculator.Calculate(BuildConfig(), Costs(), baseMargin: 0.30m, depthBonusPerLevel: 0.10m);

        Assert.Equal(1m * 1.30m, Row(rows, "rock").NewPrice);
        Assert.Equal(10m * 1.40m, Row(rows, "iron").NewPrice);
        Assert.Equal(100m * 1.50m, Row(rows, "iron-sheet").NewPrice);
    }

    /// <summary>
    /// Главное содержательное свойство: с положительной надбавкой маржа строго растёт вниз по
    /// цепочке. Именно этого не мог дать cost-plus, где автозавод зарабатывал те же 30% от своих
    /// издержек, что и рудник, а строился в 50 раз дороже.
    /// </summary>
    [Fact]
    public void Deeper_Processing_Earns_A_Strictly_Higher_Margin()
    {
        var rows = SystemSalePriceLadderCalculator.Calculate(BuildConfig(), Costs(), baseMargin: 0.20m, depthBonusPerLevel: 0.05m);

        var chain = rows.Where(r => r.SectorId == "A").OrderBy(r => r.Level).ToList();

        Assert.Equal(3, chain.Count);
        for (var i = 1; i < chain.Count; i++)
        {
            Assert.True(
                chain[i].MarginRate > chain[i - 1].MarginRate,
                $"уровень {chain[i].Level} должен иметь маржу выше уровня {chain[i - 1].Level}");
        }
    }

    /// <summary>Прибыль с единицы — это цена минус себестоимость, без скрытых слагаемых.</summary>
    [Fact]
    public void Profit_Per_Unit_Is_Simply_Price_Minus_Cost()
    {
        var rows = SystemSalePriceLadderCalculator.Calculate(BuildConfig(), Costs(), baseMargin: 0.30m, depthBonusPerLevel: 0.05m);

        Assert.All(rows, row => Assert.Equal(row.NewPrice - row.UnitCost, row.ProfitPerUnit));
    }

    /// <summary>Бесплатный материал не роняет расчёт делением на ноль — маржа у него по определению нулевая.</summary>
    [Fact]
    public void A_Zero_Cost_Material_Yields_A_Zero_Margin_Instead_Of_A_Division_By_Zero()
    {
        var costs = Costs();
        costs["rock"] = 0m;

        var rows = SystemSalePriceLadderCalculator.Calculate(BuildConfig(), costs, baseMargin: 0.30m, depthBonusPerLevel: 0m);

        Assert.Equal(0m, Row(rows, "rock").NewPrice);
        Assert.Equal(0m, Row(rows, "rock").MarginRate);
    }

    /// <summary>Порядок строк канонический (сектор → уровень → код), не порядок словаря — AGENTS §2, правило 6.</summary>
    [Fact]
    public void Rows_Come_Back_In_A_Canonical_Deterministic_Order()
    {
        var rows = SystemSalePriceLadderCalculator.Calculate(BuildConfig(), Costs(), baseMargin: 0.30m, depthBonusPerLevel: 0m);

        Assert.Equal(
            new[] { "rock", "iron", "iron-sheet", "oil", "plastic" },
            rows.Select(r => r.MaterialId).ToArray());
    }

    /// <summary>Старая цена показывается рядом с новой — предпросмотр «было/станет» до применения.</summary>
    [Fact]
    public void Each_Row_Carries_The_Previous_Price_For_Side_By_Side_Preview()
    {
        var rows = SystemSalePriceLadderCalculator.Calculate(BuildConfig(), Costs(), baseMargin: 0.30m, depthBonusPerLevel: 0m);

        Assert.Equal(0.02m, Row(rows, "rock").OldPrice);
        Assert.Equal(40m, Row(rows, "iron-sheet").OldPrice);
    }

    /// <summary>
    /// Материал с рыночной записью, но без поданной себестоимости — ошибка, а не тихо пропущенная
    /// строка: цена считается ИЗ себестоимости, и молчаливый пропуск оставил бы в боевом конфиге
    /// старое, ничем не обоснованное число.
    /// </summary>
    [Fact]
    public void A_Material_Without_A_Supplied_Cost_Is_Reported_Rather_Than_Silently_Skipped()
    {
        var costs = Costs();
        costs.Remove("plastic");

        var exception = Assert.Throws<InvalidOperationException>(
            () => SystemSalePriceLadderCalculator.Calculate(BuildConfig(), costs, baseMargin: 0.30m, depthBonusPerLevel: 0m));

        Assert.Contains("plastic", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Отрицательная наценка означала бы, что система скупает ниже себестоимости — отсекается на входе.</summary>
    [Fact]
    public void A_Negative_Base_Margin_Is_Rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SystemSalePriceLadderCalculator.Calculate(BuildConfig(), Costs(), baseMargin: -0.1m, depthBonusPerLevel: 0m));
    }

    /// <summary>Отрицательная надбавка за глубину наказывала бы переработку — ровно то, что этот блок и чинит.</summary>
    [Fact]
    public void A_Negative_Depth_Bonus_Is_Rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SystemSalePriceLadderCalculator.Calculate(BuildConfig(), Costs(), baseMargin: 0.30m, depthBonusPerLevel: -0.01m));
    }

    /// <summary>Применение переносит новые цены в конфиг и переживает полный круг сериализации/загрузки.</summary>
    [Fact]
    public void Apply_Writes_The_New_Prices_Into_A_Config_That_Still_Loads()
    {
        var config = BuildConfig();
        var rows = SystemSalePriceLadderCalculator.Calculate(config, Costs(), baseMargin: 0.30m, depthBonusPerLevel: 0.10m);

        var updated = SystemSalePriceLadderCalculator.Apply(config.Raw, rows);
        var reloaded = GameConfigLoader.Load(GameConfigWriter.Save(updated));

        Assert.Equal(14m, reloaded.Raw.Economy.BaseMarketPerMaterial.Single(m => m.MaterialId == "iron").BaseSellPrice);
        Assert.Equal(150m, reloaded.Raw.Economy.BaseMarketPerMaterial.Single(m => m.MaterialId == "iron-sheet").BaseSellPrice);
    }

    /// <summary>
    /// Ёмкость рынка — отдельный от цены параметр, лестница её не трогает (регрессия: прежняя
    /// версия инструмента считала цену ИЗ ёмкости, и легко было унаследовать эту связь обратно).
    /// </summary>
    [Fact]
    public void Apply_Leaves_Market_Capacity_Untouched()
    {
        var config = BuildConfig();
        var rows = SystemSalePriceLadderCalculator.Calculate(config, Costs(), baseMargin: 0.30m, depthBonusPerLevel: 0.10m);

        var updated = SystemSalePriceLadderCalculator.Apply(config.Raw, rows);

        Assert.Equal(
            config.Raw.Economy.BaseMarketPerMaterial.Select(m => (m.MaterialId, m.BaseCapacity)).ToArray(),
            updated.Economy.BaseMarketPerMaterial.Select(m => (m.MaterialId, m.BaseCapacity)).ToArray());
    }

    private static SystemSalePriceLadderCalculator.MaterialLadderRow Row(
        IReadOnlyList<SystemSalePriceLadderCalculator.MaterialLadderRow> rows, string materialId) =>
        rows.Single(r => r.MaterialId == materialId);
}
