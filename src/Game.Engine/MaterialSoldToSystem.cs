namespace Game.Engine;

/// <summary>
/// Команда продала материал (любого уровня передела) системе по рыночной цене (Блок 6.1, SPEC
/// §5.4): в пределах оставшейся на этот ход ёмкости — по <see cref="UnitPrice"/>, сверх —
/// с понижающим коэффициентом. Несёт уже вычисленную <see cref="MarketSaleCalculator.Calculate"/>
/// разбивку, а не пересчитывает её заново при применении — та же причина, что у
/// <see cref="EmergencyPurchased"/> и <see cref="FactoryProduced"/>. Порождается на расчёте <see
/// cref="SystemSaleStep"/> из <see cref="MaterialSaleRequested"/> (SPEC §4), в детерминированном
/// порядке команд — только так решается гонка за общую ёмкость рынка между несколькими командами
/// внутри одной фазы решений. Доступность рыночной котировки и фаза проверяются раньше, при самой
/// заявке, в <see cref="GameSession.SellToSystem"/>; достаточность склада — здесь же, на расчёте
/// (см. doc-comment <see cref="Volume"/>), а не при заявке — остаток мог измениться.
/// </summary>
public sealed record MaterialSoldToSystem : Change<GameSessionState>
{
    /// <summary>Команда-продавец.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Код проданного материала.</summary>
    public required string MaterialId { get; init; }

    /// <summary>
    /// Общий проданный объём (<see cref="WithinCapacityVolume"/> + <see cref="OverflowVolume"/>) —
    /// уже урезанный на расчёте до реального остатка на складе на тот момент (<see
    /// cref="SystemSaleStep"/>), может быть меньше заявленного в <see cref="MaterialSaleRequested"/>
    /// и даже 0. Тот же приём, что и у <see cref="LoanRepaid.Amount"/>: заявка проверяется по факту
    /// на расчёте, а не по значению, видимому в момент решения; событие всё равно порождается даже
    /// при нулевом остатке, чтобы корректно снять заявку, а не оставить её висеть на будущее.
    /// </summary>
    public required decimal Volume { get; init; }

    /// <summary>Объём, проданный в пределах ёмкости — по <see cref="UnitPrice"/>.</summary>
    public required decimal WithinCapacityVolume { get; init; }

    /// <summary>Объём сверх ёмкости — по цене с понижающим коэффициентом.</summary>
    public required decimal OverflowVolume { get; init; }

    /// <summary>Цена за единицу в пределах ёмкости (котировка × множитель маржи передела) — для аудита.</summary>
    public required decimal UnitPrice { get; init; }

    /// <summary>Итоговая выручка от продажи.</summary>
    public required decimal TotalRevenue { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        var material = state.Config.Materials[MaterialId];

        if (Volume > 0)
        {
            team.Warehouse.Remove(material, Volume);
            // TotalRevenue может обнулиться, если затяжной спад увёл цену материала в 0
            // (MarketCalculator ограничивает её снизу нулём, но не отрицательными значениями) —
            // Team.Credit(0) бросил бы.
            if (TotalRevenue > 0)
            {
                team.Credit(TotalRevenue);
            }
            state.Market.RecordSale(MaterialId, Volume);
        }
        team.ClearPendingSaleToSystem(MaterialId);
    }
}
