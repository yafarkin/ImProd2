namespace Game.Engine.Tests;

/// <summary>
/// Экзогенная цена внешней экономики (блок 11.1, <c>docs/external-economy.md</c> §2): чистые
/// функции <c>BaseSellPrice × Индекс × Эластичность</c> и зеркальной цены аварийной закупки.
/// Проверяются свойства формы кривой, а не подобранные числа — числа появятся при калибровке
/// (блок 11.8) и не должны ломать эти тесты.
/// </summary>
public class ExternalPriceCalculatorTests
{
    private const decimal Floor = 0.35m;

    // --- Эластичность: границы кривой ---

    /// <summary>Ненасыщенный рынок платит полную цену: при нулевом давлении множитель ровно 1.</summary>
    [Fact]
    public void An_Untouched_Market_Pays_The_Full_Price()
    {
        Assert.Equal(1m, ExternalPriceCalculator.ElasticityMultiplier(capacity: 100m, supplyPressure: 0m, Floor));
    }

    /// <summary>Давление, равное ёмкости, ставит цену ровно на полпути от полной до пола.</summary>
    [Fact]
    public void Pressure_Equal_To_Capacity_Puts_The_Price_Halfway_To_The_Floor()
    {
        var multiplier = ExternalPriceCalculator.ElasticityMultiplier(capacity: 100m, supplyPressure: 100m, Floor);

        Assert.Equal(Floor + (1m - Floor) / 2m, multiplier);
    }

    /// <summary>
    /// Рынок никогда не платит ноль: при сколь угодно большом предложении множитель стремится к
    /// полу, но остаётся строго выше него.
    /// </summary>
    [Fact]
    public void No_Amount_Of_Supply_Pushes_The_Price_Below_The_Floor()
    {
        var multiplier = ExternalPriceCalculator.ElasticityMultiplier(capacity: 100m, supplyPressure: 10_000_000m, Floor);

        Assert.True(multiplier > Floor, $"множитель {multiplier} обязан оставаться выше пола {Floor}");
        Assert.True(multiplier < Floor + 0.001m, $"множитель {multiplier} обязан вплотную подходить к полу {Floor}");
    }

    /// <summary>Нулевой пол — предельный случай: цена может уйти сколь угодно близко к нулю, но не к отрицательной.</summary>
    [Fact]
    public void A_Zero_Floor_Lets_The_Price_Approach_Zero_But_Never_Go_Negative()
    {
        var multiplier = ExternalPriceCalculator.ElasticityMultiplier(capacity: 1m, supplyPressure: 10_000_000m, priceFloorRate: 0m);

        Assert.True(multiplier > 0m);
        Assert.True(multiplier < 0.0001m);
    }

    /// <summary>Единичный пол выключает эластичность целиком — цена перестаёт зависеть от предложения.</summary>
    [Fact]
    public void A_Floor_Of_One_Disables_Elasticity_Entirely()
    {
        Assert.Equal(1m, ExternalPriceCalculator.ElasticityMultiplier(capacity: 100m, supplyPressure: 5000m, priceFloorRate: 1m));
    }

    // --- Эластичность: монотонность и гладкость ---

    /// <summary>
    /// Главное содержательное свойство механизма: чем больше зал уже продал, тем ниже цена. Строго
    /// монотонно на всём диапазоне, без плато и без обрыва — то, чего не давала прежняя «полка».
    /// </summary>
    [Fact]
    public void More_Supply_Always_Means_A_Strictly_Lower_Price()
    {
        var previous = decimal.MaxValue;
        for (var pressure = 0m; pressure <= 1000m; pressure += 25m)
        {
            var multiplier = ExternalPriceCalculator.ElasticityMultiplier(capacity: 100m, pressure, Floor);
            Assert.True(multiplier < previous, $"на давлении {pressure} множитель {multiplier} не ниже предыдущего {previous}");
            previous = multiplier;
        }
    }

    /// <summary>
    /// Кривая гладкая: соседние шаги давления не дают скачка цены. Регрессия на возврат «полки» —
    /// именно обрыв на границе ёмкости делал порядок команд в расчёте лотереей на крупную сумму.
    /// </summary>
    [Fact]
    public void The_Curve_Has_No_Cliff_Anywhere_Near_The_Capacity_Boundary()
    {
        for (var pressure = 90m; pressure <= 110m; pressure += 1m)
        {
            var here = ExternalPriceCalculator.ElasticityMultiplier(capacity: 100m, pressure, Floor);
            var next = ExternalPriceCalculator.ElasticityMultiplier(capacity: 100m, pressure + 1m, Floor);

            Assert.True(here - next < 0.01m, $"скачок цены {here - next} на границе ёмкости при давлении {pressure}");
        }
    }

    /// <summary>Чем шире рынок, тем меньше то же самое предложение роняет цену.</summary>
    [Fact]
    public void The_Same_Supply_Hurts_Less_On_A_Larger_Market()
    {
        var narrow = ExternalPriceCalculator.ElasticityMultiplier(capacity: 50m, supplyPressure: 100m, Floor);
        var wide = ExternalPriceCalculator.ElasticityMultiplier(capacity: 500m, supplyPressure: 100m, Floor);

        Assert.True(wide > narrow);
    }

    // --- Ёмкость и индекс ---

    /// <summary>В подъём рынок принимает больше, в спад меньше — ёмкость растянута индексом.</summary>
    [Theory]
    [InlineData(0.85, 85)]
    [InlineData(1.0, 100)]
    [InlineData(1.15, 115)]
    public void Capacity_Follows_The_Business_Activity_Index(double index, double expected)
    {
        Assert.Equal((decimal)expected, ExternalPriceCalculator.Capacity(baseCapacity: 100m, (decimal)index));
    }

    /// <summary>
    /// Калибровочная ловушка, зафиксированная тестом (<c>docs/external-economy.md</c> §2.1): индекс
    /// двигает и цену, и ёмкость, поэтому выручка при полной выборке ёмкости растёт как
    /// <b>квадрат</b> индекса. Диапазон [0.85; 1.15] даёт размах выручки 0.72–1.32; если кто-то
    /// расширит диапазон «чтобы график был выразительнее», этот тест покажет настоящую цену такого
    /// решения.
    /// </summary>
    [Fact]
    public void Revenue_At_Full_Capacity_Grows_As_The_Square_Of_The_Index()
    {
        decimal RevenueAtIndex(decimal index)
        {
            var capacity = ExternalPriceCalculator.Capacity(baseCapacity: 100m, index);
            var price = ExternalPriceCalculator.SellPrice(
                baseSellPrice: 10m, index, capacity, supplyPressure: 0m, Floor);
            return capacity * price;
        }

        var atTrough = RevenueAtIndex(0.85m);
        var atNeutral = RevenueAtIndex(1.0m);
        var atPeak = RevenueAtIndex(1.15m);

        Assert.Equal(0.7225m, atTrough / atNeutral, precision: 4);
        Assert.Equal(1.3225m, atPeak / atNeutral, precision: 4);
    }

    // --- Цена сбыта ---

    /// <summary>На нетронутом рынке цена сбыта — это ровно базовая цена, умноженная на индекс.</summary>
    [Fact]
    public void On_An_Untouched_Market_The_Sell_Price_Is_Just_Base_Times_Index()
    {
        var price = ExternalPriceCalculator.SellPrice(
            baseSellPrice: 40m, economyIndex: 1.1m, capacity: 100m, supplyPressure: 0m, Floor);

        Assert.Equal(44m, price);
    }

    /// <summary>
    /// Цена пропорциональна базовой: вдвое более дорогой материал стоит вдвое дороже при любом
    /// состоянии рынка. Это и есть то, ради чего цена сделана экзогенной — лестница цен (блок 11.2)
    /// задаёт вознаграждение за глубину передела, и рынок его не размывает.
    /// </summary>
    [Fact]
    public void The_Price_Stays_Proportional_To_The_Configured_Base_Price()
    {
        var cheap = ExternalPriceCalculator.SellPrice(10m, 1.05m, capacity: 100m, supplyPressure: 250m, Floor);
        var dear = ExternalPriceCalculator.SellPrice(20m, 1.05m, capacity: 100m, supplyPressure: 250m, Floor);

        Assert.Equal(cheap * 2m, dear);
    }

    /// <summary>Бесплатный материал остаётся бесплатным при любом рынке — вырожденный, но допустимый вход.</summary>
    [Fact]
    public void A_Zero_Base_Price_Yields_A_Zero_Sell_Price()
    {
        Assert.Equal(0m, ExternalPriceCalculator.SellPrice(0m, 1m, capacity: 100m, supplyPressure: 10m, Floor));
    }

    // --- Аварийная закупка как зеркало ---

    /// <summary>
    /// Система — маркетмейкер: покупает по цене сбыта, продаёт дороже. Полоса между двумя ценами и
    /// есть пространство P2P-торговли (§2 диагностики, «бутерброд наценок»); если она схлопнется,
    /// договариваться с другой командой станет бессмысленно.
    /// </summary>
    [Fact]
    public void The_System_Always_Sells_Dearer_Than_It_Buys()
    {
        var sell = ExternalPriceCalculator.SellPrice(
            baseSellPrice: 10m, economyIndex: 1m, capacity: 100m, supplyPressure: 0m, Floor);
        var buy = ExternalPriceCalculator.EmergencyPurchasePrice(
            baseSellPrice: 10m, economyIndex: 1m, emergencyBaseMultiplier: 1.55m,
            pressureMultiplierPerUnit: 0m, teamPurchasePressure: 0m);

        Assert.True(buy > sell, $"аварийная закупка {buy} обязана быть дороже продажи системе {sell}");
        Assert.Equal(1.55m, buy / sell);
    }

    /// <summary>
    /// Наказывается не операция, а зависимость от неё: чем больше команда закупала этот материал
    /// недавно, тем дороже следующая такая закупка (SPEC §5.3, тот же принцип, что при cost-plus).
    /// </summary>
    [Fact]
    public void Repeated_Reliance_On_Emergency_Purchase_Makes_It_Progressively_Dearer()
    {
        var previous = 0m;
        foreach (var pressure in new[] { 0m, 5m, 20m, 100m })
        {
            var price = ExternalPriceCalculator.EmergencyPurchasePrice(
                baseSellPrice: 10m, economyIndex: 1m, emergencyBaseMultiplier: 1.55m,
                pressureMultiplierPerUnit: 0.05m, teamPurchasePressure: pressure);

            Assert.True(price > previous, $"на давлении {pressure} цена {price} не выросла относительно {previous}");
            previous = price;
        }
    }

    /// <summary>
    /// Эластичность на сторону закупки намеренно не распространяется (<c>docs/external-economy.md</c>
    /// §2.3): система как поставщик бесконечна, насыщение спроса к ней не относится. Регрессия на
    /// случай, если кто-то решит «для симметрии» протащить давление предложения и сюда.
    /// </summary>
    [Fact]
    public void Emergency_Purchase_Price_Ignores_How_Much_The_Hall_Has_Been_Selling()
    {
        var price = ExternalPriceCalculator.EmergencyPurchasePrice(
            baseSellPrice: 10m, economyIndex: 1m, emergencyBaseMultiplier: 1.55m,
            pressureMultiplierPerUnit: 0m, teamPurchasePressure: 0m);

        Assert.Equal(15.5m, price);
    }

    // --- Детерминизм ---

    /// <summary>
    /// Чистая функция: одни и те же аргументы дают побитово тот же результат сколько угодно раз
    /// (AGENTS §2, правило 6 — без этого журнал не воспроизводится).
    /// </summary>
    [Fact]
    public void The_Same_Inputs_Always_Produce_The_Same_Price()
    {
        var first = ExternalPriceCalculator.SellPrice(37.5m, 1.07m, capacity: 143m, supplyPressure: 91.5m, Floor);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(first, ExternalPriceCalculator.SellPrice(37.5m, 1.07m, capacity: 143m, supplyPressure: 91.5m, Floor));
        }
    }

    // --- Защита входов ---

    /// <summary>Рынок нулевой ёмкости — ошибка конфига, а не «бесплатно всё продам»: делить не на что.</summary>
    [Fact]
    public void A_Market_With_No_Capacity_Is_Rejected_Rather_Than_Silently_Divided_By_Zero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExternalPriceCalculator.ElasticityMultiplier(capacity: 0m, supplyPressure: 1m, Floor));
    }

    /// <summary>Индекс обязан быть положительным: нулевой или отрицательный означал бы исчезнувшую экономику.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_Non_Positive_Index_Is_Rejected(double index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExternalPriceCalculator.SellPrice(10m, (decimal)index, capacity: 100m, supplyPressure: 0m, Floor));
    }

    /// <summary>Отрицательное давление означало бы «зал выкупил товар с рынка обратно» — такого события нет.</summary>
    [Fact]
    public void A_Negative_Supply_Pressure_Is_Rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExternalPriceCalculator.ElasticityMultiplier(capacity: 100m, supplyPressure: -1m, Floor));
    }

    /// <summary>Пол вне [0; 1] сломал бы монотонность кривой — отсекается на входе.</summary>
    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public void A_Floor_Outside_Zero_To_One_Is_Rejected(double floor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExternalPriceCalculator.ElasticityMultiplier(capacity: 100m, supplyPressure: 10m, (decimal)floor));
    }

    /// <summary>Система не может продавать дешевле, чем покупает — иначе появился бы вечный арбитраж.</summary>
    [Fact]
    public void An_Emergency_Multiplier_Below_One_Is_Rejected_As_A_Free_Arbitrage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExternalPriceCalculator.EmergencyPurchasePrice(10m, 1m, emergencyBaseMultiplier: 0.9m, 0m, 0m));
    }
}
