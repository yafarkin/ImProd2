using Game.Config.Economy;

namespace Game.Engine;

/// <summary>
/// «Давление предложения» — сколько единиц материала зал уже вылил на внешний рынок за последние
/// ходы, взвешенное по свежести (блок 11.4, <c>docs/external-economy.md</c> §2.2). Вход для
/// <see cref="ExternalPriceCalculator.ElasticityMultiplier"/>: чем больше давление, тем ниже цена
/// сбыта.
///
/// <para><b>Считается по всему залу, а не по команде</b> — и это главное отличие от
/// <see cref="EmergencyPurchasePressureCalculator"/>, на который класс похож всем остальным. Там
/// наказывается собственная зависимость команды от аварийных закупок, здесь моделируется насыщение
/// внешнего спроса, которому безразлично, кто именно завалил рынок. Отсюда единственный настоящий
/// рычаг конкуренции между командами одного сектора (<c>docs/levers.md</c> §1.5): сосед, продавший
/// то же самое раньше вас, портит цену вам, а вы — ему. Договориться о разнесении продаж по ходам
/// становится предметом переговоров, ради которых игра и существует.</para>
///
/// <para><b>Порядок внутри хода тоже значим.</b> Продажи текущего хода входят с полным весом
/// (возраст 0, затухание 1.0), поэтому команда, до которой очередь расчёта дошла позже
/// (<see cref="SystemSaleStep"/> идёт по возрастанию <c>Team.Id</c>), видит уже подросшее давление и
/// продаёт дешевле. Прежняя «полка» давала то же преимущество обрывом — кому достанется последняя
/// единица ёмкости по полной цене; здесь оно непрерывное и мелкое, то есть строго честнее.</para>
///
/// <para>Чистая функция от журнала, без собственного мутируемого состояния — то же самое, зачем
/// репутация и давление аварийных закупок не хранятся полями на <see cref="Game.Domain.Team"/>.</para>
/// </summary>
public static class MarketSupplyPressureCalculator
{
    /// <summary>
    /// Взвешенный по свежести объём продаж этого материала системе всеми командами зала на момент
    /// <paramref name="currentTurn"/>.
    /// </summary>
    /// <param name="entries">Журнал сессии (или его префикс — см. замечание про будущие продажи ниже).</param>
    /// <param name="materialId">Материал, чьё давление считается.</param>
    /// <param name="currentTurn">Ход, на который считается давление.</param>
    /// <param name="config">Экономика сессии — берётся <see cref="EconomyConfig.MarketSupplyPressureHalfLifeTurns"/>.</param>
    public static decimal CalculateRecentVolume(
        IReadOnlyList<EventLogEntry<GameSessionState>> entries,
        string materialId,
        int currentTurn,
        EconomyConfig config)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(materialId);
        ArgumentNullException.ThrowIfNull(config);
        if (config.MarketSupplyPressureHalfLifeTurns <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config), config.MarketSupplyPressureHalfLifeTurns,
                "Supply pressure half-life must be positive.");
        }

        var weightedVolume = 0m;

        foreach (var entry in entries)
        {
            if (entry.Change is not MaterialSoldToSystem sold
                || !string.Equals(sold.MaterialId, materialId, StringComparison.Ordinal))
            {
                continue;
            }

            // Продажи будущих ходов игнорируются, а не берутся с весом больше единицы. В обычном
            // прогоне их и не бывает, но давление осмысленно спрашивать и задним числом («какая цена
            // была на ходу 5») — при разборе партии или при восстановлении графика по журналу, где
            // журнал уже содержит все 90 ходов.
            if (sold.Turn > currentTurn)
            {
                continue;
            }

            var age = currentTurn - sold.Turn;
            var decay = (decimal)Math.Pow(0.5, (double)age / config.MarketSupplyPressureHalfLifeTurns);
            weightedVolume += sold.Volume * decay;
        }

        return weightedVolume;
    }
}
