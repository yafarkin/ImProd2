using Game.Config.Economy;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Bots.Tests;

/// <summary>
/// Поведение <see cref="SimpleBot"/> под экзогенной моделью ценообразования (блок 11.7,
/// <c>docs/external-economy.md</c> §9): цены заявок берутся от границ «окна маркетмейкера», а не от
/// себестоимости, и у бота появляется решение «продать сейчас или придержать».
/// </summary>
public class ExternalPricingBotTests
{
    /// <summary>
    /// Та же двухсекторная цепочка, что в <c>CrossSectorTradingTests</c>, но переведённая на
    /// экзогенную цену: <c>BaseSellPrice</c> заполняется лестницей от себестоимости, ёмкость взята
    /// заведомо просторной, чтобы тест проверял механику торговли, а не калибровку ёмкости.
    /// </summary>
    private static ResolvedGameConfig BuildExternalTwoSectorConfig()
    {
        var costPlus = CrossSectorTradingTests.BuildTwoSectorConfig();
        var costs = MaterialCostCalculator.CalculateAll(costPlus);
        var ladder = SystemSalePriceLadderCalculator.Calculate(costPlus, costs, baseMargin: 0.30m, depthBonusPerLevel: 0.05m);
        var raw = SystemSalePriceLadderCalculator.Apply(costPlus.Raw, ladder);

        raw = raw with { Economy = raw.Economy with { PricingModel = PricingModel.External } };
        return GameConfigLoader.Load(GameConfigWriter.Save(raw));
    }

    private static (GameSession Session, Ulid TeamA, Ulid TeamB) RunSession(ResolvedGameConfig config, int endTurn = 15)
    {
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var sectorB = config.Sectors.Single(s => s.Id == "B");
        var teamAId = Ulid.NewUlid();
        var teamBId = Ulid.NewUlid();

        var session = GameSession.StartWithEndTurn(config, endTurn, new[]
        {
            new TeamSpec { Id = teamAId, Name = "Команда А", SectorId = sectorA.Id },
            new TeamSpec { Id = teamBId, Name = "Команда Б", SectorId = sectorB.Id },
        });

        BotSessionRunner.RunToCompletion(
            session,
            new[] { new SimpleBot(teamAId, sectorA, config), new SimpleBot(teamBId, sectorB, config) },
            new Random(1));

        return (session, teamAId, teamBId);
    }

    /// <summary>
    /// Критерий готовности блока: под экзогенной ценой P2P-сделки реально заключаются. Механика
    /// контрактов не должна умирать оттого, что цена перестала выводиться из себестоимости.
    /// </summary>
    [Fact]
    public void Teams_Still_Trade_With_Each_Other_Under_External_Pricing()
    {
        var (session, _, _) = RunSession(BuildExternalTwoSectorConfig());

        Assert.True(session.State.IsFinished);
        Assert.True(session.VerifyIntegrity());

        var deliveries = session.Entries.Count(e => e.Change is ContractDelivered);
        Assert.True(deliveries > 0, "Ни одной P2P-поставки за партию — торговля между командами под экзогенной ценой не работает.");
    }

    /// <summary>
    /// Лимитная цена продажи лежит внутри окна маркетмейкера: не ниже того, что заплатит система
    /// (иначе продавать соседу бессмысленно), и не выше цены аварийной закупки (иначе бессмысленно
    /// покупать).
    /// </summary>
    [Fact]
    public void Sell_And_Buy_Limits_Stay_Inside_The_Market_Maker_Window()
    {
        var config = BuildExternalTwoSectorConfig();
        var costs = MaterialCostCalculator.CalculateAll(config);
        var floor = SystemSaleReferencePriceCalculator.CalculateAll(config, costs);
        var ceiling = SystemSaleReferencePriceCalculator.CalculateEmergencyAll(config, costs);

        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var teamAId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(config, endTurn: 15, new[]
        {
            new TeamSpec { Id = teamAId, Name = "Команда А", SectorId = sectorA.Id },
        });
        var bot = new SimpleBot(teamAId, sectorA, config);

        session.RunTick(new Random(1));
        session.AdvancePhase(PhaseTransitionTrigger.Timer);
        bot.BuildNewlyUnlockedFactories(session);

        foreach (var order in bot.ComputeSellOrders(session))
        {
            Assert.InRange(order.LimitPrice, floor[order.Material.Id], ceiling[order.Material.Id]);
        }
    }

    /// <summary>
    /// Позиции бота в окне заданы так, что продавец всегда просит меньше, чем покупатель готов
    /// заплатить — иначе стакан не сведётся ни при каких ценах. Раньше это держалось на двух вручную
    /// подобранных числах и дважды ломалось; теперь гарантируется формой параметров.
    /// </summary>
    [Fact]
    public void The_Seller_Always_Asks_Less_Than_The_Buyer_Is_Willing_To_Pay()
    {
        Assert.True(SimpleBot.SellPositionInWindow < SimpleBot.BuyPositionInWindow);
        Assert.InRange(SimpleBot.SellPositionInWindow, 0m, 1m);
        Assert.InRange(SimpleBot.BuyPositionInWindow, 0m, 1m);
    }

    /// <summary>
    /// Политика «продать сейчас или придержать» (<see cref="MarketSaleCalculator.LargestVolumeAbovePriceFloor"/>):
    /// разовый крупный излишек урезается до объёма, который не роняет цену больше чем на
    /// <see cref="SimpleBot.MinAcceptableSellPriceRate"/>, а умеренный проходит целиком.
    ///
    /// <para><b>Порог — про собственный вклад в просадку, а не про абсолютную цену.</b> Насыщение,
    /// устроенное соседями, придерживанием не лечится: склад ничего не зарабатывает и стоит денег.
    /// Бот контролирует только свой залив — его и ограничивает.</para>
    ///
    /// <para><b>Честная оговорка про силу этой политики.</b> В стационарном рынке (команда каждый
    /// ход производит и каждый ход продаёт одно и то же) придерживание почти не срабатывает и не
    /// может: отложенный объём всё равно придётся продать следующим ходом поверх нового выпуска.
    /// Политика выигрывает на разовых всплесках — накопленный склад, распродажа после простоя,
    /// подъём индекса — и именно там проверяется.</para>
    /// </summary>
    [Fact]
    public void A_One_Off_Glut_Is_Throttled_While_A_Moderate_Sale_Goes_Through_Whole()
    {
        var config = BuildExternalTwoSectorConfig();
        var economy = config.Raw.Economy;
        var material = config.Materials["ore"];
        var capacity = config.Raw.Economy.BaseMarketPerMaterial.Single(m => m.MaterialId == "ore").BaseCapacity;

        var market = new Market();
        var update = MarketCalculator.Calculate(1, economy);
        market.ReplaceQuotes(update.Quotes, update.ElectricityPrice, EconomyIndexCalculator.Calculate(1, economy));

        var costs = MaterialCostCalculator.CalculateAll(config);

        decimal Throttled(decimal desired) => MarketSaleCalculator.LargestVolumeAbovePriceFloor(
            market, costs, economy, material, desired, supplyPressure: 0m,
            SimpleBot.MinAcceptableSellPriceRate);

        var moderate = capacity * 0.1m;
        var glut = capacity * 10m;

        Assert.Equal(moderate, Throttled(moderate));
        Assert.True(Throttled(glut) < glut, "разовый залив обязан быть урезан");
        Assert.True(Throttled(glut) > 0m, "урезание не должно запрещать продажу целиком");
    }

    /// <summary>
    /// Урезанный объём и правда держит цену выше порога — регрессия на то, что бисекция сходится к
    /// верной границе, а не просто к «чему-нибудь меньшему».
    /// </summary>
    [Fact]
    public void The_Throttled_Volume_Really_Keeps_The_Price_Above_The_Threshold()
    {
        var config = BuildExternalTwoSectorConfig();
        var economy = config.Raw.Economy;
        var material = config.Materials["ore"];
        var capacity = config.Raw.Economy.BaseMarketPerMaterial.Single(m => m.MaterialId == "ore").BaseCapacity;

        var market = new Market();
        var update = MarketCalculator.Calculate(1, economy);
        market.ReplaceQuotes(update.Quotes, update.ElectricityPrice, EconomyIndexCalculator.Calculate(1, economy));

        var costs = MaterialCostCalculator.CalculateAll(config);
        var unsaturatedPrice = market.QuoteOf("ore").Price;

        var throttled = MarketSaleCalculator.LargestVolumeAbovePriceFloor(
            market, costs, economy, material, capacity * 10m, supplyPressure: 0m, SimpleBot.MinAcceptableSellPriceRate);

        var achieved = MarketSaleCalculator
            .Calculate(market, costs, economy, material, throttled, supplyPressure: 0m).UnitPrice;

        Assert.True(
            achieved >= unsaturatedPrice * SimpleBot.MinAcceptableSellPriceRate * 0.999m,
            $"после урезания цена {achieved:F4} всё ещё ниже порога от неиспорченной {unsaturatedPrice:F4}");
    }
}
