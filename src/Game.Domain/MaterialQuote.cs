namespace Game.Domain;

/// <summary>
/// Котировка материала во внешней экономике на текущий ход (SPEC §5.4): цена за единицу и
/// ёмкость — сколько единиц система выкупит по этой цене, прежде чем сработает понижающий
/// коэффициент за превышение.
/// </summary>
public sealed record MaterialQuote
{
    /// <summary>Цена за единицу.</summary>
    public decimal Price { get; }

    /// <summary>Ёмкость на этот ход.</summary>
    public decimal Capacity { get; }

    public MaterialQuote(decimal price, decimal capacity)
    {
        if (price < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), price, "Price must not be negative.");
        }
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must not be negative.");
        }

        Price = price;
        Capacity = capacity;
    }
}
