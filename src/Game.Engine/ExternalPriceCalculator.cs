namespace Game.Engine;

/// <summary>
/// Цена сделки с системой при <see cref="Game.Config.Economy.PricingModel.External"/> — экзогенная
/// модель внешней экономики (<c>docs/external-economy.md</c> §2, блок 11.1). Заменяет собой правило
/// «себестоимость × фиксированная наценка» (<see cref="MarketSaleCalculator"/>), из-за которого
/// экономика была замкнута сама на себя: цена выводилась из себестоимости, себестоимость — из цен
/// входов, внешнего якоря не было вовсе.
///
/// <para><b>Ключевое свойство: во время партии цена не зависит от себестоимости.</b>
/// <c>BaseSellPrice</c> задаётся в конфиге и считается один раз, при калибровке, инструментом
/// лестницы цен (блок 11.2). Отсюда сразу три следствия, недостижимые при cost-plus (разбор —
/// <c>docs/economy-accounting-audit.md</c>, раздел «Что из этого — фундаментальная проблема
/// дизайна»): глубина передела вознаграждается (цена растёт вниз по цепочке быстрее издержек);
/// команда, снизившая свою реальную себестоимость через R&amp;D, забирает разницу себе, а не отдаёт
/// её обратно в цену; у команд появляется сравнительное преимущество, то есть торговля между ними
/// перестаёт быть делёжкой одних и тех же процентов.</para>
///
/// <para>Чистые функции над числами — ни конфига, ни журнала, ни состояния сессии. Давление
/// предложения считается отдельно (<c>MarketSupplyPressureCalculator</c>, блок 11.4), индекс —
/// отдельно (<c>EconomyIndexCalculator</c>, блок 11.3); сюда оба приходят уже готовыми числами.
/// Подключение к тику — блок 11.5, до него этот класс не вызывается из движка.</para>
/// </summary>
public static class ExternalPriceCalculator
{
    /// <summary>
    /// Ёмкость рынка на ход: базовая из конфига, растянутая индексом деловой активности — в подъём
    /// рынок готов принять больше, в спад меньше (<c>docs/external-economy.md</c> §2.1).
    /// </summary>
    public static decimal Capacity(decimal baseCapacity, decimal economyIndex)
    {
        EnsureNonNegative(baseCapacity, nameof(baseCapacity));
        EnsurePositive(economyIndex, nameof(economyIndex));

        return baseCapacity * economyIndex;
    }

    /// <summary>
    /// Множитель эластичности спроса: во сколько раз просела цена материала оттого, что зал уже
    /// вылил на рынок <paramref name="supplyPressure"/> единиц (затухающая по полураспаду сумма
    /// продаж, не объём одного хода).
    ///
    /// <code>
    /// Эластичность = Floor + (1 − Floor) / (1 + давление / ёмкость)
    /// </code>
    ///
    /// Кривая, а не полка. До 2026-09-07 механизм был ступенькой («до ёмкости полная цена, сверх —
    /// скидка»), и плоха она не только тем, что была выключена в боевом конфиге: обрыв на границе
    /// ёмкости превращал порядок команд в расчёте (<c>SystemSaleStep</c> идёт по возрастанию
    /// <c>Team.Id</c>) в лотерею на крупную сумму — кому достанется последняя единица ёмкости по
    /// полной цене. Гладкая кривая оставляет преимущество раннему в очереди, но делает его
    /// непрерывным и мелким, то есть строго честнее прежнего поведения, а не мягче.
    /// </summary>
    /// <param name="capacity">Ёмкость рынка на этот ход (см. <see cref="Capacity"/>), строго больше нуля.</param>
    /// <param name="supplyPressure">Затухающее давление предложения зала по этому материалу, не отрицательно.</param>
    /// <param name="priceFloorRate">Пол множителя, 0..1 — ниже него цена не опускается ни при каком предложении.</param>
    public static decimal ElasticityMultiplier(decimal capacity, decimal supplyPressure, decimal priceFloorRate)
    {
        EnsurePositive(capacity, nameof(capacity));
        EnsureNonNegative(supplyPressure, nameof(supplyPressure));
        EnsureRate(priceFloorRate, nameof(priceFloorRate));

        return priceFloorRate + (1m - priceFloorRate) / (1m + supplyPressure / capacity);
    }

    /// <summary>
    /// Цена, по которой система выкупает у команды единицу материала:
    /// <c>BaseSellPrice × Индекс × Эластичность</c>.
    /// </summary>
    /// <param name="baseSellPrice">Экзогенная базовая цена материала из конфига, не отрицательна.</param>
    /// <param name="economyIndex">Индекс деловой активности на этот ход, строго больше нуля.</param>
    /// <param name="capacity">Ёмкость рынка на этот ход (см. <see cref="Capacity"/>).</param>
    /// <param name="supplyPressure">Давление предложения зала по этому материалу.</param>
    /// <param name="priceFloorRate">Пол множителя эластичности.</param>
    public static decimal SellPrice(
        decimal baseSellPrice, decimal economyIndex, decimal capacity, decimal supplyPressure, decimal priceFloorRate)
    {
        EnsureNonNegative(baseSellPrice, nameof(baseSellPrice));
        EnsurePositive(economyIndex, nameof(economyIndex));

        return baseSellPrice * economyIndex * ElasticityMultiplier(capacity, supplyPressure, priceFloorRate);
    }

    /// <summary>
    /// Средний множитель эластичности для продажи объёма <paramref name="volume"/>, начатой при
    /// давлении <paramref name="supplyPressureBefore"/> — интеграл
    /// <see cref="ElasticityMultiplier"/> по объёму, делённый на объём:
    ///
    /// <code>
    /// средняя = Floor + (1 − Floor) × (ёмкость / объём) × ln( (ёмкость + p₀ + объём) / (ёмкость + p₀) )
    /// </code>
    ///
    /// <para><b>Зачем интеграл, а не цена «на момент начала продажи».</b> Продажа сама двигает
    /// давление, и надо решить, по какой точке кривой её оценивать. Если брать давление ДО продажи,
    /// весь объём уходит по неиспорченной цене, и появляется арбитраж наоборот задуманному: выгодно
    /// вывалить всё одним заказом, потому что собственный залив себя не задевает — наказаны только
    /// соседи и будущие ходы. Если брать давление ПОСЛЕ, весь объём штрафуется по худшей точке, что
    /// избыточно сурово к первой же единице.</para>
    ///
    /// <para>Интеграл снимает вопрос целиком: каждая следующая единица продаётся во всё более
    /// насыщенный рынок, а результат <b>не зависит от того, как продажа разбита на заказы</b> — один
    /// большой заказ и десять мелких подряд дают ровно ту же выручку. Именно это свойство и нужно:
    /// механизм обязан наказывать объём, а не неумение его нарезать.</para>
    ///
    /// <para>Логарифм считается в <c>double</c> (у <c>decimal</c> его нет) — тот же приём и та же
    /// точность, что у затухания по полураспаду в
    /// <see cref="MarketSupplyPressureCalculator"/>.</para>
    /// </summary>
    public static decimal AverageElasticityMultiplier(
        decimal capacity, decimal supplyPressureBefore, decimal volume, decimal priceFloorRate)
    {
        EnsurePositive(capacity, nameof(capacity));
        EnsureNonNegative(supplyPressureBefore, nameof(supplyPressureBefore));
        EnsurePositive(volume, nameof(volume));
        EnsureRate(priceFloorRate, nameof(priceFloorRate));

        var from = capacity + supplyPressureBefore;
        var to = from + volume;
        var integralOfDecayingPart = (decimal)Math.Log((double)(to / from)) * capacity;

        return priceFloorRate + (1m - priceFloorRate) * integralOfDecayingPart / volume;
    }

    /// <summary>
    /// Средняя цена единицы при продаже объёма <paramref name="volume"/> — то же, что
    /// <see cref="SellPrice"/>, но с усреднением по объёму (см. <see cref="AverageElasticityMultiplier"/>).
    /// Именно эта цена идёт в реальную сделку; <see cref="SellPrice"/> отвечает на другой вопрос —
    /// «почём рынок берёт следующую единицу прямо сейчас», и годится для витрины котировки.
    /// </summary>
    public static decimal AverageSellPrice(
        decimal baseSellPrice, decimal economyIndex, decimal capacity,
        decimal supplyPressureBefore, decimal volume, decimal priceFloorRate)
    {
        EnsureNonNegative(baseSellPrice, nameof(baseSellPrice));
        EnsurePositive(economyIndex, nameof(economyIndex));

        return baseSellPrice * economyIndex
               * AverageElasticityMultiplier(capacity, supplyPressureBefore, volume, priceFloorRate);
    }

    /// <summary>
    /// Цена, по которой система продаёт команде единицу материала аварийно (SPEC §5.3) — зеркало
    /// <see cref="SellPrice"/>: система работает маркетмейкером, покупает по цене сбыта и продаёт
    /// дороже неё в <paramref name="emergencyBaseMultiplier"/> раз. Полоса между двумя ценами — то,
    /// в чём живёт P2P-торговля между командами, и именно её проверяет §2 диагностики («бутерброд
    /// наценок»).
    ///
    /// <para>Эластичность сюда намеренно <b>не</b> входит (<c>docs/external-economy.md</c> §2.3):
    /// она моделирует насыщение спроса, а система как поставщик бесконечна — смешивать это с
    /// персональным давлением закупок значило бы склеить две разные экономические истории в одном
    /// числе. Вместо неё действует надбавка за собственную зависимость команды от аварийных
    /// закупок, ровно та же, что и при cost-plus (<c>EmergencyPurchasePressureCalculator</c>):
    /// наказывается не сама операция, а привычка опираться на неё.</para>
    /// </summary>
    /// <param name="baseSellPrice">Экзогенная базовая цена материала из конфига.</param>
    /// <param name="economyIndex">Индекс деловой активности на этот ход.</param>
    /// <param name="emergencyBaseMultiplier">Спред системы сверх цены сбыта, ≥ 1.</param>
    /// <param name="pressureMultiplierPerUnit">Надбавка за единицу недавнего давления закупок команды.</param>
    /// <param name="teamPurchasePressure">Затухающее давление аварийных закупок этой команды по этому материалу.</param>
    public static decimal EmergencyPurchasePrice(
        decimal baseSellPrice, decimal economyIndex, decimal emergencyBaseMultiplier,
        decimal pressureMultiplierPerUnit, decimal teamPurchasePressure)
    {
        EnsureNonNegative(baseSellPrice, nameof(baseSellPrice));
        EnsurePositive(economyIndex, nameof(economyIndex));
        EnsureNonNegative(pressureMultiplierPerUnit, nameof(pressureMultiplierPerUnit));
        EnsureNonNegative(teamPurchasePressure, nameof(teamPurchasePressure));
        if (emergencyBaseMultiplier < 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(emergencyBaseMultiplier), emergencyBaseMultiplier,
                "Emergency purchase multiplier must be at least 1: the system may not sell cheaper than it buys.");
        }

        var effectiveMultiplier = emergencyBaseMultiplier + pressureMultiplierPerUnit * teamPurchasePressure;
        return baseSellPrice * economyIndex * effectiveMultiplier;
    }

    private static void EnsurePositive(decimal value, string paramName)
    {
        if (value <= 0m)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be positive.");
        }
    }

    private static void EnsureNonNegative(decimal value, string paramName)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must not be negative.");
        }
    }

    private static void EnsureRate(decimal value, string paramName)
    {
        if (value < 0m || value > 1m)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Rate must be within [0, 1].");
        }
    }
}
