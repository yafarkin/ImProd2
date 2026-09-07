namespace Game.Config.Economy;

/// <summary>
/// Один отрезок сценарного тренда экономики сессии: с хода по ход действует заданный тренд,
/// который двигает цену и ёмкость каждого материала на заданную величину за ход.
/// Числа — заглушки, требуют калибровки.
/// </summary>
public sealed record EconomyTrendPhaseConfig
{
    /// <summary>Тренд, действующий на этом отрезке.</summary>
    public required EconomyTrend Trend { get; init; }

    /// <summary>Ход, с которого начинается отрезок (включительно).</summary>
    public required int StartTurn { get; init; }

    /// <summary>Ход, которым заканчивается отрезок (включительно).</summary>
    public required int EndTurn { get; init; }

    /// <summary>
    /// Изменение цены за ход на этом отрезке.
    /// <para><b>Уходит в блоке 11.5</b> вместе с <see cref="CapacityChangePerTurn"/> — их обоих
    /// заменяет <see cref="IndexChangePerTurn"/> (состояние экономики описывается одним числом, а не
    /// двумя несогласуемыми). Пока оставлены живыми: по ним всё ещё работает
    /// <see cref="Game.Engine.MarketCalculator"/> в режиме
    /// <see cref="PricingModel.CostPlus"/>, и убрать их сейчас значило бы изменить поведение
    /// действующей игры в блоке, который заявлен как не меняющий его.</para>
    /// </summary>
    public required decimal PriceChangePerTurn { get; init; }

    /// <summary>Изменение ёмкости за ход на этом отрезке. Уходит в блоке 11.5 — см. <see cref="PriceChangePerTurn"/>.</summary>
    public required decimal CapacityChangePerTurn { get; init; }

    /// <summary>
    /// Изменение индекса деловой активности за ход на этом отрезке (блок 11.3,
    /// <c>docs/external-economy.md</c> §2.1) — единственная величина, которой сценарный тренд
    /// двигает экономику при <see cref="PricingModel.External"/>. По умолчанию 0: пока сценарии в
    /// боевых файлах не переписаны на индекс (блок 11.8), экономика по нему не движется.
    ///
    /// <para>Ограничение диапазона (<see cref="EconomyConfig.EconomyIndexMin"/>/
    /// <see cref="EconomyConfig.EconomyIndexMax"/>) накладывается на каждом ходу — почему именно
    /// так, см. doc-comment <see cref="Game.Engine.EconomyIndexCalculator"/>.</para>
    /// </summary>
    public decimal IndexChangePerTurn { get; init; }
}
