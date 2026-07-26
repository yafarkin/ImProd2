namespace Game.Bots;

/// <summary>Метрики одного хода одной партии (Блок 7.2, харнесс балансировки).</summary>
public sealed record TurnMetrics
{
    /// <summary>Ход, на который относятся метрики.</summary>
    public required int Turn { get; init; }

    /// <summary>Денежная масса на этот ход — сумма остатков всех команд (риск неконтролируемого роста).</summary>
    public required decimal TotalCash { get; init; }

    /// <summary>Объём, проданный системе за этот ход всеми командами вместе (throughput).</summary>
    public required decimal VolumeSoldToSystem { get; init; }

    /// <summary>Сколько команд на этом ходу не смогли расплатиться и получили принудительный заём (прокси «дефолта»).</summary>
    public required int ForcedLoanCount { get; init; }
}
