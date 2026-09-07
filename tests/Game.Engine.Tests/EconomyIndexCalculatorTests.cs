using Game.Config.Economy;

namespace Game.Engine.Tests;

/// <summary>
/// Индекс деловой активности (блок 11.3, <c>docs/external-economy.md</c> §2.1) — кусочно-постоянное
/// накопление по отрезкам сценарного тренда с ежеходным ограничением диапазона.
/// </summary>
public class EconomyIndexCalculatorTests
{
    private static EconomyConfig BuildEconomy(
        IReadOnlyList<EconomyTrendPhaseConfig> trend, decimal min = 0.85m, decimal max = 1.15m)
    {
        return new EconomyConfig
        {
            EmergencyPurchaseBaseMultiplier = 1m,
            EmergencyPurchasePressureMultiplierPerUnit = 0m,
            EmergencyPurchasePressureHalfLifeTurns = 1,
            BaseMarketPerMaterial = Array.Empty<MaterialMarketConfig>(),
            MarketCapacityOverflowDiscount = 1m,
            ElectricityBasePrice = 1m,
            ElectricityConsumptionPerOutputUnit = 0m,
            TrendScenario = trend,
            WarehouseLiquidationRate = 0.5m,
            EconomyIndexMin = min,
            EconomyIndexMax = max,
        };
    }

    private static EconomyTrendPhaseConfig Phase(EconomyTrend trend, int from, int to, decimal indexChange) =>
        new()
        {
            Trend = trend,
            StartTurn = from,
            EndTurn = to,
            PriceChangePerTurn = 0m,
            CapacityChangePerTurn = 0m,
            IndexChangePerTurn = indexChange,
        };

    /// <summary>Без сценария экономика стоит на нейтральном значении всю партию.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(98)]
    public void With_No_Trend_The_Economy_Stays_Neutral_On_Any_Turn(int turn)
    {
        var economy = BuildEconomy(Array.Empty<EconomyTrendPhaseConfig>());

        Assert.Equal(EconomyIndexCalculator.NeutralIndex, EconomyIndexCalculator.Calculate(turn, economy));
    }

    /// <summary>Подъём накапливается ход за ходом, начиная с первого включительно.</summary>
    [Fact]
    public void An_Upswing_Accumulates_From_The_First_Turn_Inclusive()
    {
        var economy = BuildEconomy([Phase(EconomyTrend.Up, from: 1, to: 10, indexChange: 0.01m)]);

        Assert.Equal(1.01m, EconomyIndexCalculator.Calculate(1, economy));
        Assert.Equal(1.05m, EconomyIndexCalculator.Calculate(5, economy));
        Assert.Equal(1.10m, EconomyIndexCalculator.Calculate(10, economy));
    }

    /// <summary>Вне отрезков сценария экономика не движется — достигнутое значение просто держится.</summary>
    [Fact]
    public void Outside_Every_Scenario_Phase_The_Index_Holds_Its_Last_Value()
    {
        var economy = BuildEconomy([Phase(EconomyTrend.Up, from: 1, to: 5, indexChange: 0.02m)]);

        Assert.Equal(1.10m, EconomyIndexCalculator.Calculate(5, economy));
        Assert.Equal(1.10m, EconomyIndexCalculator.Calculate(6, economy));
        Assert.Equal(1.10m, EconomyIndexCalculator.Calculate(90, economy));
    }

    /// <summary>Спад — тот же механизм с обратным знаком.</summary>
    [Fact]
    public void A_Downswing_Walks_The_Index_Back_Down()
    {
        var economy = BuildEconomy(
        [
            Phase(EconomyTrend.Up, from: 1, to: 10, indexChange: 0.01m),
            Phase(EconomyTrend.Down, from: 11, to: 20, indexChange: -0.02m),
        ]);

        Assert.Equal(1.10m, EconomyIndexCalculator.Calculate(10, economy));
        Assert.Equal(0.90m, EconomyIndexCalculator.Calculate(20, economy));
    }

    /// <summary>Границы диапазона держатся в обе стороны.</summary>
    [Fact]
    public void The_Index_Never_Leaves_Its_Configured_Range()
    {
        var crash = BuildEconomy([Phase(EconomyTrend.Down, from: 1, to: 90, indexChange: -0.05m)]);
        var boom = BuildEconomy([Phase(EconomyTrend.Up, from: 1, to: 90, indexChange: 0.05m)]);

        Assert.Equal(0.85m, EconomyIndexCalculator.Calculate(90, crash));
        Assert.Equal(1.15m, EconomyIndexCalculator.Calculate(90, boom));
    }

    /// <summary>
    /// Ключевое содержательное решение блока: ограничение накладывается на КАЖДОМ ходу, а не один
    /// раз на итоговую сумму. Сценарий ниже сначала уводит сырое значение далеко за пол
    /// (−0.05 × 10 ходов = 0.5 при поле 0.85), затем разворачивается вверх.
    ///
    /// <para>При ежеходном ограничении восстановление начинается прямо от пола, и уже через два хода
    /// подъёма индекс заметно выше пола — ровно тогда, когда об этом сказала новостная лента. При
    /// ограничении «в конце» экономика тащила бы невидимый игроку долг (0.5 → 0.6 всё ещё ниже пола)
    /// и первые семь ходов объявленного подъёма стояла бы на месте, делая ленту лгущей.</para>
    /// </summary>
    [Fact]
    public void Recovery_Starts_From_The_Floor_Instead_Of_Repaying_An_Invisible_Debt()
    {
        var economy = BuildEconomy(
        [
            Phase(EconomyTrend.Down, from: 1, to: 10, indexChange: -0.05m),
            Phase(EconomyTrend.Up, from: 11, to: 20, indexChange: 0.05m),
        ]);

        Assert.Equal(0.85m, EconomyIndexCalculator.Calculate(10, economy));
        Assert.Equal(0.95m, EconomyIndexCalculator.Calculate(12, economy));
        Assert.Equal(1.15m, EconomyIndexCalculator.Calculate(16, economy));
    }

    /// <summary>Одна и та же пара (ход, конфиг) всегда даёт один и тот же индекс — AGENTS §2, правило 6.</summary>
    [Fact]
    public void The_Same_Turn_Always_Yields_The_Same_Index()
    {
        var economy = BuildEconomy([Phase(EconomyTrend.Up, from: 3, to: 40, indexChange: 0.003m)]);
        var first = EconomyIndexCalculator.Calculate(37, economy);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(first, EconomyIndexCalculator.Calculate(37, economy));
        }
    }

    /// <summary>Нулевой или отрицательный ход бессмысленен — партия начинается с первого.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_Non_Positive_Turn_Is_Rejected(int turn)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EconomyIndexCalculator.Calculate(turn, BuildEconomy(Array.Empty<EconomyTrendPhaseConfig>())));
    }

    /// <summary>Перевёрнутый диапазон — ошибка конфига, а не молча пустой интервал.</summary>
    [Fact]
    public void An_Inverted_Range_Is_Rejected()
    {
        var economy = BuildEconomy(Array.Empty<EconomyTrendPhaseConfig>(), min: 1.2m, max: 0.9m);

        Assert.Throws<ArgumentOutOfRangeException>(() => EconomyIndexCalculator.Calculate(1, economy));
    }

    /// <summary>Неположительный пол означал бы исчезнувшую экономику — цена стала бы нулевой.</summary>
    [Fact]
    public void A_Non_Positive_Lower_Bound_Is_Rejected()
    {
        var economy = BuildEconomy(Array.Empty<EconomyTrendPhaseConfig>(), min: 0m, max: 1.15m);

        Assert.Throws<ArgumentOutOfRangeException>(() => EconomyIndexCalculator.Calculate(1, economy));
    }
}
