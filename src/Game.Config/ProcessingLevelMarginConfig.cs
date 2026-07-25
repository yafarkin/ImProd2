namespace Game.Config;

/// <summary>
/// Множитель маржи при продаже системе для заданного уровня передела (SPEC §5.4): продукция
/// более высокого передела продаётся с большей наценкой — стимул подниматься по цепочке.
/// Значение — заглушка, требует калибровки.
/// </summary>
public sealed record ProcessingLevelMarginConfig
{
    /// <summary>Уровень передела материала.</summary>
    public required int Level { get; init; }

    /// <summary>Множитель к базовой рыночной цене при продаже материала этого уровня.</summary>
    public required decimal MarginMultiplier { get; init; }
}
