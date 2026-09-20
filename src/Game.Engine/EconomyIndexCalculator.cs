using Game.Config.Economy;

namespace Game.Engine;

/// <summary>
/// Индекс деловой активности — скаляр состояния внешней экономики на заданный ход (блок 11.3,
/// <c>docs/external-economy.md</c> §2.1). Тот самый «общий коэффициент», по которому игрок видит,
/// куда движется экономика: он же двигает цену и ёмкость сбыта (с блока 11.5), он же является
/// предметом новостной ленты и графика на большом экране.
///
/// <para>Накопление кусочно-постоянное по отрезкам сценарного тренда — тот же приём, что и в
/// <see cref="MarketCalculator"/>: ход вне всех отрезков сценария экономику не двигает. Заменяет
/// собой две независимые дельты прежней модели (<c>PriceChangePerTurn</c> и
/// <c>CapacityChangePerTurn</c>): состояние экономики — одно число, а не два несогласуемых.</para>
///
/// <para><b>Ограничение диапазона накладывается на КАЖДОМ ходу, а не один раз в конце.</b> Это
/// содержательное решение, а не деталь реализации. Если копить сумму без ограничения и обрезать
/// только результат, экономика накапливает невидимый игроку «долг»: сценарий, уронивший сырое
/// значение до 0.5 при поле 0.85, потом отыгрывает первые 0.35 роста вхолостую — лента новостей
/// сообщает о подъёме, а индекс на экране стоит на месте. При ежеходном ограничении восстановление
/// начинается прямо от пола, то есть ровно тогда, когда об этом сказали новости.</para>
///
/// <para>Чистая функция от (ход, конфиг) — не зависит ни от журнала, ни от действий команд, поэтому
/// безопасна для повторного вызова на один и тот же ход (AGENTS §2, правило 6). Действия команд
/// влияют на цену отдельно, через давление предложения
/// (<see cref="ExternalPriceCalculator.ElasticityMultiplier"/>), а не через индекс.</para>
/// </summary>
public static class EconomyIndexCalculator
{
    /// <summary>Нейтральное состояние экономики — значение индекса до всякого тренда, на начало партии.</summary>
    public const decimal NeutralIndex = 1.0m;

    public static decimal Calculate(int turn, EconomyConfig economy)
    {
        ArgumentNullException.ThrowIfNull(economy);
        if (turn <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(turn), turn, "Turn must be positive.");
        }
        if (economy.EconomyIndexMin <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(economy), economy.EconomyIndexMin, "Economy index lower bound must be positive.");
        }
        if (economy.EconomyIndexMin > economy.EconomyIndexMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(economy), economy.EconomyIndexMin, "Economy index lower bound must not exceed the upper bound.");
        }

        var index = Math.Clamp(NeutralIndex, economy.EconomyIndexMin, economy.EconomyIndexMax);

        for (var t = 1; t <= turn; t++)
        {
            var phase = economy.TrendScenario.FirstOrDefault(p => t >= p.StartTurn && t <= p.EndTurn);
            if (phase is null)
            {
                continue;
            }

            index = Math.Clamp(index + phase.IndexChangePerTurn, economy.EconomyIndexMin, economy.EconomyIndexMax);
        }

        return index;
    }
}
