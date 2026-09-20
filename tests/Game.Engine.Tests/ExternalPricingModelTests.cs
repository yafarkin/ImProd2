using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>
/// Включение экзогенной модели ценообразования в движок (блок 11.5,
/// <c>docs/external-economy.md</c> §8): ветвление по <see cref="PricingModel"/> в двух точках —
/// продаже системе и аварийной закупке — плюс ёмкость и котировка по индексу.
/// </summary>
public class ExternalPricingModelTests
{
    private const string Ore = "ore";

    private static EconomyConfig External(decimal floor = 0.35m) =>
        TestGameConfig.Resolved.Raw.Economy with
        {
            PricingModel = PricingModel.External,
            MarketPriceFloorRate = floor,
            MarketSupplyPressureHalfLifeTurns = 3,
        };

    private static Market MarketAt(EconomyConfig economy, int turn)
    {
        var market = new Market();
        var update = MarketCalculator.Calculate(turn, economy);
        market.ReplaceQuotes(update.Quotes, update.ElectricityPrice, EconomyIndexCalculator.Calculate(turn, economy));
        return market;
    }

    private static Material OreMaterial => TestGameConfig.Resolved.Materials[Ore];

    // --- Котировка ---

    /// <summary>
    /// Публикуемая цена — это <c>BaseSellPrice × Индекс</c>, то есть цена НЕНАСЫЩЕННОГО рынка.
    /// Просадку за перепроизводство накладывает уже сама продажа: котировка обязана оставаться
    /// чистой функцией от хода, иначе её нельзя пересчитать по журналу.
    /// </summary>
    [Fact]
    public void The_Published_Quote_Is_The_Price_Of_An_Unsaturated_Market()
    {
        var quote = MarketAt(External(), turn: 1).QuoteOf(Ore);

        // BaseSellPrice руды в TestGameConfig — 10, индекс без сценария нейтральный.
        Assert.Equal(10m, quote.Price);
    }

    /// <summary>Себестоимость на цену больше не влияет вовсе — в этом весь смысл экзогенного якоря.</summary>
    [Fact]
    public void The_Sale_Price_No_Longer_Depends_On_Cost()
    {
        var economy = External();
        var market = MarketAt(economy, turn: 1);

        var withRealCosts = MarketSaleCalculator.Calculate(
            market, TestGameConfig.MaterialCosts, economy, OreMaterial, volume: 1m);
        var withAbsurdCosts = MarketSaleCalculator.Calculate(
            market, new Dictionary<string, decimal> { [Ore] = 999_999m }, economy, OreMaterial, volume: 1m);

        Assert.Equal(withRealCosts.TotalRevenue, withAbsurdCosts.TotalRevenue);
    }

    /// <summary>Электричество трендом не движется: индекс двигает только сторону спроса.</summary>
    [Fact]
    public void Electricity_Stays_At_Its_Base_Price_Regardless_Of_The_Economy_Index()
    {
        var economy = External() with
        {
            TrendScenario =
            [
                new EconomyTrendPhaseConfig
                {
                    Trend = EconomyTrend.Up, StartTurn = 1, EndTurn = 90,
                    PriceChangePerTurn = 5m, CapacityChangePerTurn = 5m, IndexChangePerTurn = 0.01m,
                },
            ],
        };

        var update = MarketCalculator.Calculate(turn: 10, economy);

        Assert.Equal(economy.ElectricityBasePrice, update.ElectricityPrice);
    }

    // --- Продажа ---

    /// <summary>
    /// Котировка — это <b>предельная</b> цена (почём рынок берёт следующую единицу), а цена сделки —
    /// <b>средняя</b> по проданному объёму, и она всегда строго ниже: продажа сама насыщает рынок,
    /// начиная с первой же единицы. Разница тем меньше, чем меньше объём относительно ёмкости.
    ///
    /// <para>Свойство не косметическое — именно оно делает интегральную оценку честной. Если бы
    /// сделка шла по котировке, объём продавался бы по цене, действующей только для первой единицы,
    /// и залив рынка не стоил бы продавцу ничего (см. doc-comment
    /// <see cref="ExternalPriceCalculator.AverageElasticityMultiplier"/>).</para>
    /// </summary>
    [Fact]
    public void The_Deal_Price_Is_The_Average_Along_The_Curve_And_Sits_Just_Below_The_Quote()
    {
        var economy = External();
        var market = MarketAt(economy, turn: 1);
        var quotedPrice = market.QuoteOf(Ore).Price;

        var tiny = MarketSaleCalculator.Calculate(
            market, TestGameConfig.MaterialCosts, economy, OreMaterial, volume: 0.01m, supplyPressure: 0m);
        var small = MarketSaleCalculator.Calculate(
            market, TestGameConfig.MaterialCosts, economy, OreMaterial, volume: 1m, supplyPressure: 0m);

        Assert.True(small.UnitPrice < quotedPrice, "средняя цена сделки обязана быть ниже предельной котировки");
        Assert.True(small.UnitPrice > quotedPrice * 0.99m, "на объёме в 1% ёмкости разрыв обязан быть крошечным");
        Assert.True(tiny.UnitPrice > small.UnitPrice, "чем меньше объём, тем ближе средняя цена к котировке");
    }

    /// <summary>Чем сильнее насыщен рынок, тем ниже средняя цена сделки.</summary>
    [Fact]
    public void A_More_Saturated_Market_Pays_Less_For_The_Same_Volume()
    {
        var economy = External();
        var market = MarketAt(economy, turn: 1);

        var early = MarketSaleCalculator.Calculate(
            market, TestGameConfig.MaterialCosts, economy, OreMaterial, volume: 10m, supplyPressure: 0m);
        var late = MarketSaleCalculator.Calculate(
            market, TestGameConfig.MaterialCosts, economy, OreMaterial, volume: 10m, supplyPressure: 500m);

        Assert.True(late.UnitPrice < early.UnitPrice);
    }

    /// <summary>
    /// Ключевое свойство интегральной оценки: выручка не зависит от того, как продажа нарезана на
    /// заказы. Один заказ на 100 единиц и четыре по 25 подряд дают одно и то же — механизм наказывает
    /// объём, а не неумение его нарезать. Без интеграла здесь появился бы арбитраж в ту или другую
    /// сторону (см. doc-comment <see cref="ExternalPriceCalculator.AverageElasticityMultiplier"/>).
    /// </summary>
    [Fact]
    public void Revenue_Does_Not_Depend_On_How_The_Sale_Is_Split_Into_Orders()
    {
        var economy = External();
        var market = MarketAt(economy, turn: 1);

        var oneBigOrder = MarketSaleCalculator.Calculate(
            market, TestGameConfig.MaterialCosts, economy, OreMaterial, volume: 100m, supplyPressure: 0m).TotalRevenue;

        var manySmallOrders = 0m;
        var pressure = 0m;
        for (var i = 0; i < 4; i++)
        {
            manySmallOrders += MarketSaleCalculator.Calculate(
                market, TestGameConfig.MaterialCosts, economy, OreMaterial, volume: 25m, supplyPressure: pressure).TotalRevenue;
            pressure += 25m;
        }

        Assert.Equal(oneBigOrder, manySmallOrders, precision: 6);
    }

    /// <summary>Даже при чудовищном заливе выручка положительна: пол цены не даёт рынку платить ноль.</summary>
    [Fact]
    public void Even_A_Catastrophic_Glut_Still_Pays_Something()
    {
        var economy = External(floor: 0.35m);
        var market = MarketAt(economy, turn: 1);

        var sale = MarketSaleCalculator.Calculate(
            market, TestGameConfig.MaterialCosts, economy, OreMaterial, volume: 10m, supplyPressure: 1_000_000m);

        Assert.True(sale.UnitPrice > 10m * 0.35m);
        Assert.True(sale.UnitPrice < 10m * 0.36m);
    }

    /// <summary>
    /// <c>OverflowVolume</c> под внешней моделью — не ценовой тариф, а сигнал интерфейсу «столько
    /// ушло за ёмкость рынка». Цена при этом остаётся непрерывной, отдельного тарифа нет.
    /// </summary>
    [Fact]
    public void Overflow_Volume_Reports_Saturation_Without_Introducing_A_Second_Price_Tier()
    {
        var economy = External();
        var market = MarketAt(economy, turn: 1);
        var capacity = market.QuoteOf(Ore).Capacity;

        var sale = MarketSaleCalculator.Calculate(
            market, TestGameConfig.MaterialCosts, economy, OreMaterial, volume: capacity + 40m, supplyPressure: 0m);

        Assert.Equal(capacity, sale.WithinCapacityVolume);
        Assert.Equal(40m, sale.OverflowVolume);
        Assert.Equal(sale.TotalRevenue, (sale.WithinCapacityVolume + sale.OverflowVolume) * sale.UnitPrice, precision: 6);
    }

    /// <summary>Материал без ёмкости — ошибка конфига с внятным текстом, а не бесшумно кривая цена.</summary>
    [Fact]
    public void A_Material_Without_Market_Capacity_Is_Reported_Loudly()
    {
        var economy = External() with
        {
            BaseMarketPerMaterial = [new MaterialMarketConfig { MaterialId = Ore, BaseSellPrice = 10m, BaseCapacity = 0m }],
        };
        var market = MarketAt(economy, turn: 1);

        var exception = Assert.Throws<InvalidOperationException>(
            () => MarketSaleCalculator.Calculate(market, TestGameConfig.MaterialCosts, economy, OreMaterial, volume: 1m));

        Assert.Contains("capacity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    // --- Рычаг «насколько резко рынок реагирует на объём» ---

    /// <summary>
    /// <c>MarketCapacityScale</c> двигает общую жёсткость реакции, не трогая относительную структуру
    /// цепочки: ёмкость каждого материала умножается на один и тот же множитель, поэтому соотношения
    /// между материалами (у сырья ёмкость кратно больше, чем у флагмана) сохраняются.
    /// </summary>
    [Fact]
    public void The_Capacity_Scale_Moves_All_Materials_By_The_Same_Factor()
    {
        var baseline = MarketAt(External(), turn: 1);
        var doubled = MarketAt(External() with { MarketCapacityScale = 2m }, turn: 1);

        foreach (var entry in TestGameConfig.Resolved.Raw.Economy.BaseMarketPerMaterial)
        {
            Assert.Equal(baseline.QuoteOf(entry.MaterialId).Capacity * 2m, doubled.QuoteOf(entry.MaterialId).Capacity);
        }
    }

    /// <summary>Узкий рынок реагирует резче: тот же объём роняет цену сильнее.</summary>
    [Fact]
    public void A_Narrower_Market_Reacts_More_Sharply_To_The_Same_Volume()
    {
        decimal PriceAtScale(decimal scale)
        {
            var economy = External() with { MarketCapacityScale = scale };
            return MarketSaleCalculator.Calculate(
                MarketAt(economy, 1), TestGameConfig.MaterialCosts, economy, OreMaterial, volume: 100m).UnitPrice;
        }

        var sharp = PriceAtScale(0.5m);
        var normal = PriceAtScale(1m);
        var forgiving = PriceAtScale(10m);

        Assert.True(sharp < normal, "узкий рынок обязан реагировать резче обычного");
        Assert.True(forgiving > normal, "ёмкий рынок обязан реагировать мягче обычного");
    }

    /// <summary>
    /// Точный выключатель эластичности — <c>MarketPriceFloorRate = 1.0</c>: цена перестаёт зависеть
    /// от объёма вовсе, ровно как до Фазы 11. Регрессия на то, что «безразличный рынок» остаётся
    /// достижим одной строкой конфига.
    /// </summary>
    [Fact]
    public void A_Floor_Of_One_Makes_The_Market_Completely_Indifferent_To_Volume()
    {
        var economy = External(floor: 1m);
        var market = MarketAt(economy, turn: 1);

        var tiny = MarketSaleCalculator.Calculate(market, TestGameConfig.MaterialCosts, economy, OreMaterial, 0.01m);
        var enormous = MarketSaleCalculator.Calculate(market, TestGameConfig.MaterialCosts, economy, OreMaterial, 1_000_000m);

        Assert.Equal(tiny.UnitPrice, enormous.UnitPrice);
        Assert.Equal(market.QuoteOf(Ore).Price, enormous.UnitPrice);
    }

    /// <summary>
    /// Нулевой масштаб — не «бесконечно ёмкий рынок», а деление на ноль в кривой; отсекается с
    /// подсказкой, чем выключать эластичность на самом деле.
    /// </summary>
    [Fact]
    public void A_Non_Positive_Capacity_Scale_Is_Rejected_With_A_Hint()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => MarketCalculator.Calculate(1, External() with { MarketCapacityScale = 0m }));

        Assert.Contains("MarketPriceFloorRate", exception.Message, StringComparison.Ordinal);
    }

    // --- Аварийная закупка ---

    /// <summary>
    /// Система остаётся маркетмейкером: покупает по цене сбыта, продаёт дороже неё ровно во столько
    /// раз, во сколько задано конфигом. Полоса между двумя ценами — пространство P2P-торговли.
    /// </summary>
    [Fact]
    public void The_System_Still_Sells_Dearer_Than_It_Buys()
    {
        var economy = External() with
        {
            EmergencyPurchaseBaseMultiplier = 1.55m,
            EmergencyPurchasePressureMultiplierPerUnit = 0m,
        };
        var (log, team) = TestGameConfig.StartSessionWithOneTeam(startingCash: 100_000m);
        var market = MarketAt(economy, turn: 1);

        log.Append(new EmergencyPurchaseRequested { Id = Ulid.NewUlid(), TeamId = team.Id, MaterialId = Ore, Volume = 1m });
        var changes = EmergencyPurchaseStep.Run(
            log.State.Teams[team.Id], TestGameConfig.MaterialCosts, economy, log.Entries, currentTurn: 1, market);

        var purchased = Assert.IsType<EmergencyPurchased>(Assert.Single(changes));
        Assert.Equal(10m * 1.55m, purchased.UnitPrice);
    }

    /// <summary>Аварийная закупка не дешевеет оттого, что зал завалил рынок этим же материалом (§2.3).</summary>
    [Fact]
    public void Emergency_Purchase_Ignores_How_Much_The_Hall_Has_Been_Selling()
    {
        var economy = External() with
        {
            EmergencyPurchaseBaseMultiplier = 1.55m,
            EmergencyPurchasePressureMultiplierPerUnit = 0m,
        };
        var (log, team) = TestGameConfig.StartSessionWithOneTeam(startingCash: 100_000m);
        var market = MarketAt(economy, turn: 1);

        log.Append(new EmergencyPurchased
        {
            Id = Ulid.NewUlid(), Turn = 1, TeamId = team.Id, MaterialId = Ore, Volume = 500m, UnitPrice = 1m, TotalCost = 500m,
        });
        log.Append(new MaterialSoldToSystem
        {
            Id = Ulid.NewUlid(), TeamId = team.Id, MaterialId = Ore, Volume = 500m,
            WithinCapacityVolume = 500m, OverflowVolume = 0m, UnitPrice = 1m, TotalRevenue = 500m, Turn = 1,
        });
        log.Append(new EmergencyPurchaseRequested { Id = Ulid.NewUlid(), TeamId = team.Id, MaterialId = Ore, Volume = 1m });

        var changes = EmergencyPurchaseStep.Run(
            log.State.Teams[team.Id], TestGameConfig.MaterialCosts, economy, log.Entries, currentTurn: 1, market);

        var purchased = Assert.IsType<EmergencyPurchased>(Assert.Single(changes));
        Assert.Equal(10m * 1.55m, purchased.UnitPrice);
    }

    // --- Цена до заявки (блок 11.10) ---

    /// <summary>
    /// <see cref="MarketSaleCalculator.MarginalUnitPrice"/> — предел средней цены сделки при объёме,
    /// стремящемся к нулю. Это та величина, которую видит игрок как «цена без вашей заявки», и тот
    /// же эталон, от которого считается порог придерживания у бота и идеального зала.
    /// </summary>
    [Fact]
    public void The_Marginal_Price_Is_The_Limit_Of_The_Deal_Price_As_The_Volume_Goes_To_Zero()
    {
        var economy = External();
        var market = MarketAt(economy, turn: 1);

        var marginal = MarketSaleCalculator.MarginalUnitPrice(
            market, TestGameConfig.MaterialCosts, economy, OreMaterial, supplyPressure: 40m);
        var tinySale = MarketSaleCalculator.Calculate(
            market, TestGameConfig.MaterialCosts, economy, OreMaterial, volume: 0.0001m, supplyPressure: 40m);

        // Не тождество, а предел: продажа на 0.0001 единицы — уже усреднение по кривой, поэтому
        // совпадение ожидается с точностью порядка размера этого объёма, а не до последнего знака.
        Assert.Equal((double)marginal, (double)tinySale.UnitPrice, precision: 5);
        Assert.True(tinySale.UnitPrice < marginal); // и всегда чуть ниже — цена падает уже на первой единице
    }

    /// <summary>
    /// Средняя цена сделки всегда строго ниже цены до заявки — сама заявка и есть то, что просаживает
    /// цену. Именно эту разницу показывает предпросмотр продажи на /team.
    /// </summary>
    [Fact]
    public void A_Real_Order_Always_Sells_Below_The_Price_That_Preceded_It()
    {
        var economy = External();
        var market = MarketAt(economy, turn: 1);
        var capacity = market.QuoteOf(Ore).Capacity;

        var marginal = MarketSaleCalculator.MarginalUnitPrice(
            market, TestGameConfig.MaterialCosts, economy, OreMaterial, supplyPressure: 0m);
        var small = MarketSaleCalculator.Calculate(
            market, TestGameConfig.MaterialCosts, economy, OreMaterial, capacity * 0.1m, supplyPressure: 0m);
        var large = MarketSaleCalculator.Calculate(
            market, TestGameConfig.MaterialCosts, economy, OreMaterial, capacity * 2m, supplyPressure: 0m);

        Assert.True(small.UnitPrice < marginal);
        Assert.True(large.UnitPrice < small.UnitPrice); // чем крупнее залив, тем сильнее сам себе портит цену
    }

    /// <summary>
    /// Под cost-plus цена от объёма не зависит вовсе, поэтому «цена до заявки» совпадает с ценой
    /// сделки — предпросмотр в этом режиме просадки не показывает, и показывать нечего.
    /// </summary>
    [Fact]
    public void Under_Cost_Plus_The_Marginal_Price_Equals_The_Deal_Price()
    {
        var economy = TestGameConfig.Resolved.Raw.Economy;
        var market = MarketAt(economy, turn: 1);

        var marginal = MarketSaleCalculator.MarginalUnitPrice(
            market, TestGameConfig.MaterialCosts, economy, OreMaterial, supplyPressure: 999m);
        var sale = MarketSaleCalculator.Calculate(
            market, TestGameConfig.MaterialCosts, economy, OreMaterial, volume: 500m, supplyPressure: 999m);

        Assert.Equal(sale.UnitPrice, marginal);
    }

    // --- Режим по умолчанию ---

    /// <summary>
    /// Действующая игра остаётся на cost-plus, пока сессионный файл явно не скажет иначе: экзогенная
    /// модель станет рабочей только после перекалибровки обеих цепочек (блок 11.8).
    /// </summary>
    [Fact]
    public void The_Default_Pricing_Model_Is_Still_Cost_Plus()
    {
        Assert.Equal(PricingModel.CostPlus, TestGameConfig.Resolved.Raw.Economy.PricingModel);
    }

    /// <summary>
    /// Cost-plus ведёт себя ровно как раньше — регрессия на то, что ветвление не задело
    /// действующую модель: цена равна себестоимости × 1.30 и от рыночной котировки не зависит.
    /// </summary>
    [Fact]
    public void Cost_Plus_Keeps_Pricing_From_Cost_Exactly_As_Before()
    {
        var economy = TestGameConfig.Resolved.Raw.Economy;
        var market = MarketAt(economy, turn: 1);
        var unitCost = TestGameConfig.MaterialCosts[Ore];

        var sale = MarketSaleCalculator.Calculate(
            market, TestGameConfig.MaterialCosts, economy, OreMaterial, volume: 1m, supplyPressure: 999m);

        Assert.Equal(unitCost * MarketSaleCalculator.SystemSaleMarginMultiplier, sale.UnitPrice);
    }
}
