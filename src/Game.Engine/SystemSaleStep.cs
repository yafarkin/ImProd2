using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Разрешение заявок на продажу материала системе, объявленных за прошедшую фазу решений (SPEC §4,
/// §5.4): по каждому заявленному материалу урезает объём до реального остатка на складе на этот
/// момент расчёта (заявка могла быть подана по остатку, который с тех пор не изменился, но проверка
/// всё равно на расчёте, не при заявке), считает разбивку по <see cref="MarketSaleCalculator"/> и порождает <see
/// cref="MaterialSoldToSystem"/>. Вызывается <see cref="GameSession.RunTick"/> для каждой команды по
/// очереди, по возрастанию <see cref="Team.Id"/> (как и остальные шаги тика) — именно порядок команд
/// здесь и решает гонку за общую ёмкость рынка (SPEC §4): раньше в очереди команда получает ёмкость
/// по полной цене первой, инструмент арбитража времени решения (кто раньше нажал) убран. Материалы
/// одной команды перебираются по коду (детерминированно, не по порядку словаря — AGENTS §2, правило
/// 6). Вызывается после <see cref="EmergencyPurchaseStep"/> той же команды и до расчёта производства
/// — продать в этот же ход можно только то, что было на складе до него, не свежий выпуск (SPEC §4).
/// Возвращает готовые события, не применяет их.
/// </summary>
public static class SystemSaleStep
{
    public static IReadOnlyList<Change<GameSessionState>> Run(
        Team team, Market market, IReadOnlyDictionary<string, decimal> materialCosts, EconomyConfig economy,
        IReadOnlyDictionary<string, Material> materials)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(materialCosts);
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(materials);

        var changes = new List<Change<GameSessionState>>();

        foreach (var (materialId, requestedVolume) in team.PendingSaleVolumeByMaterial.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var material = materials[materialId];
            var volume = Math.Min(requestedVolume, team.Warehouse.QuantityOf(material));

            if (volume <= 0)
            {
                // Заявка была, но продавать нечего — событие с нулевым объёмом всё равно нужно, чтобы
                // корректно снять заявку (см. doc-comment MaterialSoldToSystem.Volume), не пересчитывая
                // MarketSaleCalculator (он сам бросает на нулевой объём).
                changes.Add(new MaterialSoldToSystem
                {
                    Id = Ulid.NewUlid(),
                    TeamId = team.Id,
                    MaterialId = materialId,
                    Volume = 0m,
                    WithinCapacityVolume = 0m,
                    OverflowVolume = 0m,
                    UnitPrice = 0m,
                    TotalRevenue = 0m,
                });
                continue;
            }

            var sale = MarketSaleCalculator.Calculate(market, materialCosts, economy, material, volume);

            changes.Add(new MaterialSoldToSystem
            {
                Id = Ulid.NewUlid(),
                TeamId = team.Id,
                MaterialId = materialId,
                Volume = volume,
                WithinCapacityVolume = sale.WithinCapacityVolume,
                OverflowVolume = sale.OverflowVolume,
                UnitPrice = sale.UnitPrice,
                TotalRevenue = sale.TotalRevenue,
            });
        }

        return changes;
    }
}
