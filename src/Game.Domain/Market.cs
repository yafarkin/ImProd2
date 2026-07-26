namespace Game.Domain;

/// <summary>
/// Внешняя экономика сессии (SPEC §5.4-5.5): по каждому материалу — текущая котировка (цена,
/// ёмкость), плюс цена электричества как отдельного системного ресурса (потребляется фабриками,
/// но не продаётся системе, поэтому у него нет ёмкости). Котировки целиком заменяются раз за ход
/// (см. движок, шаг обновления рынка) — счётчик проданного за ход объёма при этом обнуляется,
/// поэтому оставшаяся ёмкость всегда отсчитывается от котировки текущего хода.
/// </summary>
public sealed class Market
{
    private readonly Dictionary<string, MaterialQuote> _quotes = new();
    private readonly Dictionary<string, decimal> _soldThisTurn = new();

    /// <summary>Цена электричества на текущий ход.</summary>
    public decimal ElectricityPrice { get; private set; }

    /// <summary>Есть ли котировка материала (заполняется только после первого обновления рынка).</summary>
    public bool HasQuote(string materialId) => _quotes.ContainsKey(materialId);

    /// <summary>Текущая котировка материала; бросает исключение, если рынок ещё не публиковал её.</summary>
    public MaterialQuote QuoteOf(string materialId)
    {
        if (!_quotes.TryGetValue(materialId, out var quote))
        {
            throw new InvalidOperationException($"No market quote available for material '{materialId}'.");
        }

        return quote;
    }

    /// <summary>Сколько единиц материала уже продано системе в этом ходу.</summary>
    public decimal SoldThisTurn(string materialId) => _soldThisTurn.TryGetValue(materialId, out var sold) ? sold : 0m;

    /// <summary>Оставшаяся в этом ходу ёмкость материала по полной цене (никогда не отрицательна).</summary>
    public decimal RemainingCapacityOf(string materialId) => Math.Max(0m, QuoteOf(materialId).Capacity - SoldThisTurn(materialId));

    /// <summary>
    /// Заменяет котировки на новый ход и обнуляет счётчик проданного объёма — вызывается только из
    /// события обновления рынка движка (для первого хода — из события начала сессии).
    /// </summary>
    public void ReplaceQuotes(IReadOnlyDictionary<string, MaterialQuote> quotes, decimal electricityPrice)
    {
        ArgumentNullException.ThrowIfNull(quotes);

        _quotes.Clear();
        foreach (var (materialId, quote) in quotes)
        {
            _quotes[materialId] = quote;
        }

        _soldThisTurn.Clear();
        ElectricityPrice = electricityPrice;
    }

    /// <summary>Учитывает продажу материала системе в счётчике объёма этого хода; вызывается только из события продажи движка.</summary>
    public void RecordSale(string materialId, decimal volume)
    {
        if (volume <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "Sale volume must be positive.");
        }

        _soldThisTurn[materialId] = SoldThisTurn(materialId) + volume;
    }
}
