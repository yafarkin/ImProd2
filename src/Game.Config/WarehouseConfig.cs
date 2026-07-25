namespace Game.Config;

/// <summary>
/// Параметры склада (SPEC §5.7): бесплатный лимит вместимости + плата за превышение.
/// Числа — заглушки, требуют калибровки.
/// </summary>
public sealed record WarehouseConfig
{
    /// <summary>Бесплатный лимит вместимости склада (единиц продукции, суммарно по всем материалам).</summary>
    public required decimal FreeCapacity { get; init; }

    /// <summary>Плата за единицу хранения сверх бесплатного лимита, за ход.</summary>
    public required decimal OverageFeePerUnit { get; init; }
}
