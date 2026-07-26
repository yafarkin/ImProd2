namespace Game.Domain;

/// <summary>
/// Неизменяемые условия сделки (SPEC §6): объём, тип продукции, цена за единицу, штраф за срыв,
/// ход вступления в силу, срок. Изменение условий контракта после подписания невозможно — новые
/// условия оформляются как новый контракт взамен расторгнутого. Record — чтобы две независимо
/// поданные заявки можно было сравнить на совпадение через обычное структурное равенство.
/// </summary>
public sealed record ContractTerms
{
    /// <summary>Тип контракта — разовая поставка или регулярные поставки.</summary>
    public ContractType Type { get; }

    /// <summary>Поставляемый материал.</summary>
    public Material Material { get; }

    /// <summary>Объём поставки (для recurring — за одну поставку).</summary>
    public decimal Volume { get; }

    /// <summary>Цена за единицу материала.</summary>
    public decimal UnitPrice { get; }

    /// <summary>Штраф за срыв поставки (доля от суммы поставки, 0..1).</summary>
    public decimal PenaltyRate { get; }

    /// <summary>Ход, с которого контракт вступает в силу.</summary>
    public int EffectiveTurn { get; }

    /// <summary>Ход поставки — только для <see cref="ContractType.Spot"/>.</summary>
    public int? SpotDeliveryTurn { get; }

    /// <summary>Последний ход действия регулярных поставок — только для <see cref="ContractType.Recurring"/>.</summary>
    public int? RecurringEndTurn { get; }

    public ContractTerms(
        ContractType type,
        Material material,
        decimal volume,
        decimal unitPrice,
        decimal penaltyRate,
        int effectiveTurn,
        int? spotDeliveryTurn,
        int? recurringEndTurn)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (volume <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "Contract volume must be positive.");
        }
        if (unitPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), unitPrice, "Unit price must be positive.");
        }
        if (penaltyRate < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(penaltyRate), penaltyRate, "Penalty rate must not be negative.");
        }
        if (effectiveTurn <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(effectiveTurn), effectiveTurn, "Effective turn must be positive.");
        }

        if (type == ContractType.Spot)
        {
            if (spotDeliveryTurn is null or <= 0)
            {
                throw new ArgumentException("Spot contracts require a positive delivery turn.", nameof(spotDeliveryTurn));
            }
            if (recurringEndTurn is not null)
            {
                throw new ArgumentException("Spot contracts must not specify a recurring end turn.", nameof(recurringEndTurn));
            }
            if (spotDeliveryTurn < effectiveTurn)
            {
                throw new ArgumentException("Delivery turn must not precede the effective turn.", nameof(spotDeliveryTurn));
            }
        }
        else
        {
            if (recurringEndTurn is null or <= 0)
            {
                throw new ArgumentException("Recurring contracts require a positive end turn.", nameof(recurringEndTurn));
            }
            if (spotDeliveryTurn is not null)
            {
                throw new ArgumentException("Recurring contracts must not specify a spot delivery turn.", nameof(spotDeliveryTurn));
            }
            if (recurringEndTurn < effectiveTurn)
            {
                throw new ArgumentException("End turn must not precede the effective turn.", nameof(recurringEndTurn));
            }
        }

        Type = type;
        Material = material;
        Volume = volume;
        UnitPrice = unitPrice;
        PenaltyRate = penaltyRate;
        EffectiveTurn = effectiveTurn;
        SpotDeliveryTurn = spotDeliveryTurn;
        RecurringEndTurn = recurringEndTurn;
    }
}
