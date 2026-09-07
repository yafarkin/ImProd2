using Game.Config.Economy;

namespace Game.Engine.Tests;

/// <summary>
/// Давление предложения (блок 11.4, <c>docs/external-economy.md</c> §2.2) — затухающая по
/// полураспаду сумма продаж материала системе <b>всем залом</b>. Вход для эластичности цены.
/// </summary>
public class MarketSupplyPressureCalculatorTests
{
    private static EconomyConfig Economy(int halfLifeTurns = 3) =>
        TestGameConfig.Resolved.Raw.Economy with { MarketSupplyPressureHalfLifeTurns = halfLifeTurns };

    /// <summary>
    /// Кладёт материал на склад команды, чтобы продажу было чем обеспечить (<c>log.Append</c>
    /// применяет событие, а не только записывает его). Тот же приём, что в соседних тестах истории.
    /// </summary>
    private static void Stock(EventLog<GameSessionState> log, Ulid teamId, string materialId, decimal volume) =>
        log.Append(new EmergencyPurchased
        {
            Id = Ulid.NewUlid(),
            Turn = 1,
            TeamId = teamId,
            MaterialId = materialId,
            Volume = volume,
            UnitPrice = 1m,
            TotalCost = volume,
        });

    private static MaterialSoldToSystem Sale(Ulid teamId, string materialId, decimal volume, int turn) =>
        new()
        {
            Id = Ulid.NewUlid(),
            TeamId = teamId,
            MaterialId = materialId,
            Volume = volume,
            WithinCapacityVolume = volume,
            OverflowVolume = 0m,
            UnitPrice = 1m,
            TotalRevenue = volume,
            Turn = turn,
        };

    /// <summary>Нетронутый рынок — нулевое давление, а не «немного».</summary>
    [Fact]
    public void A_Market_Nobody_Sold_Into_Has_No_Pressure()
    {
        var (log, _) = TestGameConfig.StartSessionWithOneTeam();

        Assert.Equal(0m, MarketSupplyPressureCalculator.CalculateRecentVolume(log.Entries, "ore", 1, Economy()));
    }

    /// <summary>Продажа этого же хода входит с полным весом — рынок реагирует сразу, а не со следующего хода.</summary>
    [Fact]
    public void A_Sale_Made_This_Very_Turn_Counts_In_Full()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam(startingCash: 100_000m);
        Stock(log, team.Id, "ore", 40m);
        log.Append(Sale(team.Id, "ore", volume: 40m, turn: 1));

        Assert.Equal(40m, MarketSupplyPressureCalculator.CalculateRecentVolume(log.Entries, "ore", 1, Economy()));
    }

    /// <summary>
    /// Главное отличие от давления аварийных закупок: считается по всему залу. Насыщению внешнего
    /// спроса безразлично, кто именно завалил рынок — отсюда и берётся конкуренция между командами
    /// одного сектора.
    /// </summary>
    [Fact]
    public void Pressure_Sums_Across_All_Teams_Not_Just_One()
    {
        var (log, buyer, seller) = TestGameConfig.StartSessionWithTwoTeams(startingCash: 100_000m);
        Stock(log, buyer.Id, "ore", 30m);
        Stock(log, seller.Id, "ore", 50m);
        log.Append(Sale(buyer.Id, "ore", volume: 30m, turn: 1));
        log.Append(Sale(seller.Id, "ore", volume: 50m, turn: 1));

        Assert.Equal(80m, MarketSupplyPressureCalculator.CalculateRecentVolume(log.Entries, "ore", 1, Economy()));
    }

    /// <summary>Давление ведётся по каждому материалу отдельно — залив рудой не роняет цену листа.</summary>
    [Fact]
    public void Flooding_One_Material_Leaves_Another_Untouched()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam(startingCash: 100_000m);
        Stock(log, team.Id, "ore", 500m);
        log.Append(Sale(team.Id, "ore", volume: 500m, turn: 1));

        Assert.Equal(0m, MarketSupplyPressureCalculator.CalculateRecentVolume(log.Entries, "sheet", 1, Economy()));
    }

    /// <summary>Через период полураспада вклад продажи ровно вдвое меньше, через два — вчетверо.</summary>
    [Fact]
    public void A_Sale_Loses_Exactly_Half_Its_Weight_Every_Half_Life()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam(startingCash: 100_000m);
        Stock(log, team.Id, "ore", 100m);
        log.Append(Sale(team.Id, "ore", volume: 100m, turn: 1));
        var economy = Economy(halfLifeTurns: 3);

        Assert.Equal(100m, MarketSupplyPressureCalculator.CalculateRecentVolume(log.Entries, "ore", 1, economy));
        Assert.Equal(50m, MarketSupplyPressureCalculator.CalculateRecentVolume(log.Entries, "ore", 4, economy), precision: 6);
        Assert.Equal(25m, MarketSupplyPressureCalculator.CalculateRecentVolume(log.Entries, "ore", 7, economy), precision: 6);
    }

    /// <summary>
    /// Рынок «переваривает» залив: несколько ходов простоя — и цена возвращается почти к базовой.
    /// Это и делает придерживание склада осмысленным решением игрока.
    /// </summary>
    [Fact]
    public void Standing_Idle_For_Long_Enough_Lets_The_Market_Recover()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam(startingCash: 100_000m);
        Stock(log, team.Id, "ore", 1000m);
        log.Append(Sale(team.Id, "ore", volume: 1000m, turn: 1));

        var afterLongIdle = MarketSupplyPressureCalculator.CalculateRecentVolume(log.Entries, "ore", 31, Economy(3));

        Assert.True(afterLongIdle < 1m, $"через 30 ходов простоя давление {afterLongIdle} обязано практически исчезнуть");
    }

    /// <summary>Давление накапливается по ходам, а не сбрасывается каждый ход, — у рынка есть память.</summary>
    [Fact]
    public void Pressure_Accumulates_Across_Turns_Instead_Of_Resetting()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam(startingCash: 100_000m);
        Stock(log, team.Id, "ore", 200m);
        log.Append(Sale(team.Id, "ore", volume: 100m, turn: 1));
        log.Append(Sale(team.Id, "ore", volume: 100m, turn: 2));

        var pressure = MarketSupplyPressureCalculator.CalculateRecentVolume(log.Entries, "ore", 2, Economy(3));

        Assert.True(pressure > 100m, $"давление {pressure} обязано превышать объём одного хода");
    }

    /// <summary>Снятая заявка (нулевой объём) на рынок не давит.</summary>
    [Fact]
    public void A_Zero_Volume_Sale_Adds_No_Pressure()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam(startingCash: 100_000m);
        log.Append(Sale(team.Id, "ore", volume: 0m, turn: 1));

        Assert.Equal(0m, MarketSupplyPressureCalculator.CalculateRecentVolume(log.Entries, "ore", 1, Economy()));
    }

    /// <summary>
    /// Давление можно спросить задним числом: продажи более поздних ходов не учитываются и не
    /// получают вес больше единицы. Нужно при разборе партии и при восстановлении графика по
    /// журналу, где журнал уже содержит все ходы до конца.
    /// </summary>
    [Fact]
    public void Sales_From_Future_Turns_Are_Ignored_Rather_Than_Weighted_Above_One()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam(startingCash: 100_000m);
        Stock(log, team.Id, "ore", 100m);
        log.Append(Sale(team.Id, "ore", volume: 100m, turn: 10));

        Assert.Equal(0m, MarketSupplyPressureCalculator.CalculateRecentVolume(log.Entries, "ore", 5, Economy()));
    }

    /// <summary>Нулевой период полураспада означал бы деление на ноль в показателе — отсекается на входе.</summary>
    [Fact]
    public void A_Non_Positive_Half_Life_Is_Rejected()
    {
        var (log, _) = TestGameConfig.StartSessionWithOneTeam();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => MarketSupplyPressureCalculator.CalculateRecentVolume(log.Entries, "ore", 1, Economy(halfLifeTurns: 0)));
    }

    /// <summary>Один и тот же журнал всегда даёт одно и то же давление — AGENTS §2, правило 6.</summary>
    [Fact]
    public void The_Same_Journal_Always_Yields_The_Same_Pressure()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam(startingCash: 100_000m);
        Stock(log, team.Id, "ore", 37.5m);
        log.Append(Sale(team.Id, "ore", volume: 37.5m, turn: 2));
        var first = MarketSupplyPressureCalculator.CalculateRecentVolume(log.Entries, "ore", 6, Economy());

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(first, MarketSupplyPressureCalculator.CalculateRecentVolume(log.Entries, "ore", 6, Economy()));
        }
    }

    /// <summary>
    /// Критерий готовности блока (<c>docs/external-economy.md</c> §9): две команды, продавшие
    /// одинаковый объём в одном ходу, получают предсказуемо разную цену — та, до которой очередь
    /// расчёта дошла позже, видит уже подросшее давление.
    ///
    /// <para>Это и есть механизм конкуренции за сбыт внутри сектора. Прежняя «полка» давала то же
    /// преимущество обрывом (кому достанется последняя единица ёмкости по полной цене); здесь оно
    /// непрерывное и мелкое — строго честнее, а не мягче, и потому проверяется не только знак
    /// разницы, но и её умеренность.</para>
    /// </summary>
    [Fact]
    public void Selling_Later_In_The_Turn_Order_Fetches_A_Predictably_Lower_Price()
    {
        var (log, first, second) = TestGameConfig.StartSessionWithTwoTeams(startingCash: 100_000m);
        var economy = Economy();
        const decimal Capacity = 100m;
        const decimal Volume = 50m;

        decimal PriceForNextSeller() => ExternalPriceCalculator.SellPrice(
            baseSellPrice: 10m,
            economyIndex: 1m,
            capacity: Capacity,
            supplyPressure: MarketSupplyPressureCalculator.CalculateRecentVolume(log.Entries, "ore", 1, economy),
            priceFloorRate: 0.35m);

        Stock(log, first.Id, "ore", Volume);
        Stock(log, second.Id, "ore", Volume);

        var firstPrice = PriceForNextSeller();
        log.Append(Sale(first.Id, "ore", Volume, turn: 1));

        var secondPrice = PriceForNextSeller();
        log.Append(Sale(second.Id, "ore", Volume, turn: 1));

        Assert.Equal(10m, firstPrice);
        Assert.True(secondPrice < firstPrice, "вторая команда обязана продать дешевле первой");
        Assert.True(
            secondPrice > firstPrice * 0.75m,
            $"преимущество очереди обязано оставаться умеренным: {firstPrice} → {secondPrice}");
    }
}
