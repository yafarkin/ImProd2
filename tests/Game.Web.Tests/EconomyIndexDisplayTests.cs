using Game.Config.Economy;
using Game.Config.Loading;
using Game.Engine;

namespace Game.Web.Tests;

/// <summary>
/// Индекс деловой активности в интерфейсе (блок 11.10, <c>docs/external-economy.md</c> §6): число с
/// направлением и график истории на большом экране и у команды.
/// </summary>
public class EconomyIndexDisplayTests
{
    private static EconomyIndexHistoryCalculator.IndexPoint Point(int turn, decimal index) => new(turn, index);

    [Fact]
    public void An_Empty_History_Has_No_Data_To_Show()
    {
        var summary = EconomyIndexDisplay.Describe([]);

        Assert.False(summary.HasData);
    }

    [Fact]
    public void The_Arrow_Follows_The_Change_Against_The_Previous_Turn()
    {
        var rising = EconomyIndexDisplay.Describe([Point(1, 1.00m), Point(2, 1.04m)]);
        var falling = EconomyIndexDisplay.Describe([Point(1, 1.04m), Point(2, 1.00m)]);
        var flat = EconomyIndexDisplay.Describe([Point(1, 1.00m), Point(2, 1.00m)]);

        Assert.Equal("↑", rising.Arrow);
        Assert.Equal(0.04m, rising.Change);
        Assert.Equal("↓", falling.Arrow);
        Assert.Equal("→", flat.Arrow);
    }

    /// <summary>
    /// Первый ход партии: направления ещё нет (сравнивать не с чем), но само значение показывается —
    /// иначе индекс появлялся бы у игрока только со второго хода.
    /// </summary>
    [Fact]
    public void A_Single_Point_Shows_The_Value_With_No_Direction()
    {
        var summary = EconomyIndexDisplay.Describe([Point(1, 0.97m)]);

        Assert.True(summary.HasData);
        Assert.Equal(0.97m, summary.Index);
        Assert.Null(summary.PreviousIndex);
        Assert.Equal("→", summary.Arrow);
    }

    /// <summary>Формат не должен зависеть от культуры — приложение работает в invariant-режиме.</summary>
    [Fact]
    public void The_Value_Is_Formatted_With_Two_Decimals()
    {
        Assert.Equal("1.03", EconomyIndexDisplay.Describe([Point(1, 1.0345m)]).ValueText);
        Assert.Equal("0.85", EconomyIndexDisplay.Describe([Point(1, 0.85m)]).ValueText);
    }

    [Fact]
    public void The_Level_Text_Compares_The_Index_To_The_Neutral_Level()
    {
        Assert.Contains("выше", EconomyIndexDisplay.Describe([Point(1, 1.10m)]).LevelText);
        Assert.Contains("ниже", EconomyIndexDisplay.Describe([Point(1, 0.90m)]).LevelText);
        Assert.Contains("нейтральном", EconomyIndexDisplay.Describe([Point(1, EconomyIndexCalculator.NeutralIndex)]).LevelText);
    }

    /// <summary>
    /// Под cost-plus индекс публикуется, но ни на что не влияет — показывать его игроку значило бы
    /// предложить строить решения на величине, которая ничего не делает.
    /// </summary>
    [Fact]
    public void The_Index_Is_Shown_Only_Under_The_External_Pricing_Model()
    {
        var economy = ShippedEconomy();

        Assert.True(EconomyIndexDisplay.IsVisible(economy with { PricingModel = PricingModel.External }));
        Assert.False(EconomyIndexDisplay.IsVisible(economy with { PricingModel = PricingModel.CostPlus }));
    }

    [Fact]
    public void The_Chart_Draws_One_Line_Through_All_The_Points()
    {
        var layout = EconomyIndexDisplay.BuildChart(
            [Point(1, 1.00m), Point(2, 1.02m), Point(3, 0.99m)], 900, 240);

        var series = Assert.Single(layout.Series);
        Assert.Equal("0.99", series.LastValueLabel);
        Assert.NotEmpty(series.PathData);
    }

    private static EconomyConfig ShippedEconomy() => GameConfigLoader.LoadFromFiles(
        Path.Combine(AppContext.BaseDirectory, "Samples", "production-models", "training-1-sector.json"),
        Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json")).Raw.Economy;
}
