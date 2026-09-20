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
    /// Изменение цены за ход на этом отрезке — <b>только при <see cref="PricingModel.CostPlus"/></b>.
    /// <para>При <see cref="PricingModel.External"/> не используется вовсе: состояние экономики там
    /// описывается одним числом (<see cref="IndexChangePerTurn"/>), а не двумя несогласуемыми
    /// абсолютными приращениями. Поле не удалено, потому что <c>CostPlus</c> сохранён как
    /// калибровочно-регрессионный режим (<c>docs/TODO.md</c> №27) — вместе с ним живут и его
    /// настройки.</para>
    /// </summary>
    public required decimal PriceChangePerTurn { get; init; }

    /// <summary>Изменение ёмкости за ход на этом отрезке — только при <see cref="PricingModel.CostPlus"/>, см. <see cref="PriceChangePerTurn"/>.</summary>
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
