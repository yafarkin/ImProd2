using Game.Config.Economy;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Web.Tests;

/// <summary>
/// Предпросмотр продажи системе на /team (блок 11.10, <c>docs/external-economy.md</c> §6): игрок
/// видит среднюю цену своей заявки и то, насколько заявка сама просаживает цену, — до подачи, а не
/// по факту зачисления.
/// </summary>
public class MarketSalePreviewTests
{
    private static readonly IReadOnlyList<EventLogEntry<GameSessionState>> NoEntries =
        Array.Empty<EventLogEntry<GameSessionState>>();

    private static ResolvedGameConfig ShippedConfig() => GameConfigLoader.LoadFromFiles(
        Path.Combine(AppContext.BaseDirectory, "Samples", "production-models", "training-1-sector.json"),
        Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));

    private static (Market Market, EconomyConfig Economy, Material Material, IReadOnlyDictionary<string, decimal> Costs, decimal Capacity)
        Setup(PricingModel model)
    {
        var config = ShippedConfig();
        var economy = config.Raw.Economy with { PricingModel = model };
        var market = new Market();
        var update = MarketCalculator.Calculate(1, economy);
        market.ReplaceQuotes(update.Quotes, update.ElectricityPrice, EconomyIndexCalculator.Calculate(1, economy));

        var material = config.Materials.Values.First(m => m.Level == 0 && market.HasQuote(m.Id));
        return (market, economy, material, MaterialCostCalculator.CalculateAll(config), market.QuoteOf(material.Id).Capacity);
    }

    [Fact]
    public void A_Volume_Of_Zero_Has_Nothing_To_Preview()
    {
        var (market, economy, material, costs, _) = Setup(PricingModel.External);

        var preview = MarketSalePreview.Calculate(
            market, costs, economy, material, volume: 0m, realUnitCost: 0m, NoEntries, currentTurn: 1);

        Assert.False(preview.HasQuote);
    }

    /// <summary>
    /// Ядро блока: заявка продаётся ниже цены, что была до неё, и чем крупнее заявка, тем сильнее
    /// просадка. Без этой подписи игрок узнавал бы о заливе рынка только по зачислению.
    /// </summary>
    [Fact]
    public void A_Bigger_Order_Sells_Below_The_Price_That_Preceded_It_And_Drops_It_Further()
    {
        var (market, economy, material, costs, capacity) = Setup(PricingModel.External);

        var small = MarketSalePreview.Calculate(
            market, costs, economy, material, capacity * 0.05m, realUnitCost: 0m, NoEntries, currentTurn: 1);
        var large = MarketSalePreview.Calculate(
            market, costs, economy, material, capacity * 3m, realUnitCost: 0m, NoEntries, currentTurn: 1);

        Assert.Equal(small.MarginalUnitPrice, large.MarginalUnitPrice); // рынок до заявки один и тот же
        Assert.True(small.UnitPrice < small.MarginalUnitPrice);
        Assert.True(large.PriceImpactRate > small.PriceImpactRate);
        Assert.InRange(large.PriceImpactRate, 0m, 1m);
    }

    /// <summary>
    /// Доход считается по средней цене всего объёма, прибыль — по реальной себестоимости остатка;
    /// предпросмотр не может разойтись с фактом, потому что зовёт ту же функцию, что и расчёт.
    /// </summary>
    [Fact]
    public void Revenue_And_Profit_Follow_The_Average_Price_And_The_Real_Unit_Cost()
    {
        var (market, economy, material, costs, capacity) = Setup(PricingModel.External);
        var volume = capacity * 0.5m;

        var preview = MarketSalePreview.Calculate(
            market, costs, economy, material, volume, realUnitCost: 1m, NoEntries, currentTurn: 1);

        Assert.Equal(volume * preview.UnitPrice, preview.Revenue);
        Assert.Equal(preview.Revenue - volume, preview.Profit);
    }

    /// <summary>Под cost-plus цена от объёма не зависит — просадки нет и показывать её не нужно.</summary>
    [Fact]
    public void Under_Cost_Plus_There_Is_No_Price_Impact_To_Show()
    {
        var (market, economy, material, costs, capacity) = Setup(PricingModel.CostPlus);

        var preview = MarketSalePreview.Calculate(
            market, costs, economy, material, capacity * 10m, realUnitCost: 0m, NoEntries, currentTurn: 1);

        Assert.Equal(0m, preview.PriceImpactRate);
        Assert.Equal(preview.MarginalUnitPrice, preview.UnitPrice);
    }

    /// <summary>
    /// Давление предложения — чужие продажи этого же хода — попадает в предпросмотр: цена «до вашей
    /// заявки» уже учитывает насыщение, устроенное залом.
    /// </summary>
    [Fact]
    public void Sales_Already_Made_By_The_Hall_Lower_The_Price_Before_The_Order_Even_Starts()
    {
        var (market, economy, material, costs, capacity) = Setup(PricingModel.External);

        var clean = MarketSalePreview.Calculate(
            market, costs, economy, material, volume: 1m, realUnitCost: 0m, NoEntries, currentTurn: 1);

        // Запись собирается вручную, а не через живой журнал: давление предложения — чистая функция
        // от последовательности событий, состояние сессии ей не нужно.
        var hallSale = new EventLogEntry<GameSessionState>
        {
            SequenceNumber = 0,
            Timestamp = DateTimeOffset.UnixEpoch,
            PreviousHash = string.Empty,
            Hash = string.Empty,
            Change = new MaterialSoldToSystem
            {
                Id = Ulid.NewUlid(), TeamId = Ulid.NewUlid(), MaterialId = material.Id, Volume = capacity * 2m,
                WithinCapacityVolume = capacity * 2m, OverflowVolume = 0m, UnitPrice = 1m, TotalRevenue = 1m, Turn = 1,
            },
        };

        var saturated = MarketSalePreview.Calculate(
            market, costs, economy, material, volume: 1m, realUnitCost: 0m, [hallSale], currentTurn: 1);

        Assert.True(saturated.MarginalUnitPrice < clean.MarginalUnitPrice);
    }
}
