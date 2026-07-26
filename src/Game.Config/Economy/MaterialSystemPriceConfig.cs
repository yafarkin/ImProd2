namespace Game.Config.Economy;

/// <summary>
/// Временная системная цена материала — заглушка под рыночную цену, пока рынок (Блок 6.1) не
/// реализован. Аварийная закупка (SPEC §5.3) считает цену как <c>Price</c> ×
/// <see cref="EconomyConfig.EmergencyPurchasePriceMultiplier"/>. Когда появится функция экономики
/// (цена, ёмкость по каждому материалу на текущий ход), это поле уступит место ей.
/// </summary>
public sealed record MaterialSystemPriceConfig
{
    /// <summary>Код материала (<see cref="Game.Config.Catalog.MaterialConfig.Id"/>).</summary>
    public required string MaterialId { get; init; }

    /// <summary>Базовая системная цена за единицу — основа для аварийной закупки.</summary>
    public required decimal Price { get; init; }
}
