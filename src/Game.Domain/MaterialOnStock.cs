namespace Game.Domain;

/// <summary>
/// Остаток одного материала на складе команды, вместе с его реальной себестоимостью — суммой
/// денег, фактически вложенных в то, что сейчас лежит на складе (зарплата рабочих за
/// добычу/производство, реально уплаченная цена закупки или поставки по контракту; рыночные цены
/// сюда не подмешиваются — это отдельная, прогнозная метрика, см. <c>FactoryProfitabilityCalculator</c>).
/// Учёт — методом скользящего среднего (moving average cost): каждое поступление добавляет свою
/// стоимость к <see cref="TotalCostBasis"/>, каждое списание уменьшает её пропорционально доле
/// списанного количества, так что <see cref="AverageUnitCost"/> всегда отражает реальную среднюю
/// цену того, что осталось.
/// </summary>
public sealed class MaterialOnStock
{
    /// <summary>Материал, для которого учитывается остаток.</summary>
    public Material Material { get; }

    /// <summary>Текущее количество на складе; никогда не отрицательно.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Суммарная реальная стоимость текущего остатка; никогда не отрицательна.</summary>
    public decimal TotalCostBasis { get; private set; }

    /// <summary>Средняя реальная себестоимость единицы остатка; 0, если остатка нет.</summary>
    public decimal AverageUnitCost => Quantity > 0 ? TotalCostBasis / Quantity : 0m;

    public MaterialOnStock(Material material, decimal quantity = 0m, decimal costBasis = 0m)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Stock quantity must not be negative.");
        }
        if (costBasis < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(costBasis), costBasis, "Cost basis must not be negative.");
        }

        Material = material;
        Quantity = quantity;
        TotalCostBasis = costBasis;
    }

    /// <summary>Увеличивает остаток на положительное количество вместе с его реальной стоимостью (0, если поступление было бесплатным).</summary>
    public void Add(decimal amount, decimal cost)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be positive.");
        }
        if (cost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cost), cost, "Cost must not be negative.");
        }

        Quantity += amount;
        TotalCostBasis += cost;
    }

    /// <summary>
    /// Расхождение между запрошенным и фактическим остатком в пределах этой величины считается шумом
    /// decimal-округления (два математически эквивалентных, но по-разному упорядоченных умножения —
    /// например, расчёт выхода производства и расчёт расхода сырья на него, см. <c>ProductionCalculator</c>
    /// — расходятся в последнем знаке), а не настоящей нехваткой, и просто урезается до фактического
    /// остатка вместо исключения (обнаружено на реальном многосекторном конфиге, Блок 7.3.3, запрос —
    /// «не должен падать» — прим. на порядки грубее самого наблюдавшегося расхождения ~1e-26).
    /// </summary>
    private const decimal RoundingNoiseTolerance = 0.0000000001m;

    /// <summary>
    /// Уменьшает остаток; бросает исключение, если запрошено больше, чем есть в наличии, — за
    /// вычетом шума decimal-округления, см. <see cref="RoundingNoiseTolerance"/>. Возвращает реальную
    /// себестоимость списанной части (по среднему на момент списания) — вызывающий код использует её,
    /// чтобы перенести реальную стоимость дальше (в новую партию продукции) или посчитать реальную
    /// прибыль от продажи.
    /// </summary>
    public decimal Remove(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be positive.");
        }
        if (amount > Quantity)
        {
            if (amount - Quantity > RoundingNoiseTolerance)
            {
                throw new InvalidOperationException(
                    $"Not enough '{Material.Id}' in stock: requested {amount}, have {Quantity}.");
            }

            amount = Quantity;
        }

        var removedCost = TotalCostBasis * amount / Quantity;
        Quantity -= amount;
        TotalCostBasis -= removedCost;

        // Защита от дрейфа decimal-округления: на нулевом остатке обе величины должны быть ровно 0.
        if (Quantity == 0)
        {
            TotalCostBasis = 0m;
        }

        return removedCost;
    }
}
