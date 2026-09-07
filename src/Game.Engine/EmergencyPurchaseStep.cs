using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Разрешение заявок на аварийную закупку, объявленных за прошедшую фазу решений (SPEC §4, §5.3).
/// Ветвится по <see cref="PricingModel"/> — вторая (и последняя) точка движка, где модель
/// ценообразования различима, <c>docs/external-economy.md</c> §8.
///
/// <para><b><see cref="PricingModel.External"/></b> (блок 11.5) — цена зеркальна продаже: система
/// работает маркетмейкером, покупает по цене сбыта и продаёт дороже неё в
/// <see cref="EconomyConfig.EmergencyPurchaseBaseMultiplier"/> раз. Берётся котировка рынка
/// (<c>BaseSellPrice × Индекс</c>, см. <see cref="MarketCalculator"/>) — но <b>без</b> эластичности:
/// та моделирует насыщение спроса, а система как поставщик бесконечна
/// (<c>docs/external-economy.md</c> §2.3). Надбавка за собственное давление закупок команды
/// действует в обеих моделях одинаково.</para>
///
/// <para><b><see cref="PricingModel.CostPlus"/></b> — прежнее правило:
/// по каждому заявленному материалу считает цену (себестоимость, <see cref="MaterialCostCalculator"/>
/// — не рыночная котировка, запрос пользователя, rebalance/2-sector-stepwise, 2026-08-21 — × множитель,
/// с надбавкой за «давление» недавних закупок — по фактической истории уже применённых <see
/// cref="EmergencyPurchased"/>, та же формула, что раньше считалась при самой заявке в <see
/// cref="GameSession.EmergencyPurchase"/>) и порождает <see cref="EmergencyPurchased"/>.</para>
///
/// <para>Множитель —
/// намеренно большой (SPEC §5.3: это аварийный, невыгодный план, стимул договариваться с другими
/// командами через стакан, а не опираться на него). Материалы команды перебираются в детерминированном
/// порядке (по коду, не по порядку словаря — AGENTS §2, правило 6); порядок команд между собой не
/// важен — цена зависит только от истории самой команды, ни от каких других. Вызывается <see
/// cref="GameSession.RunTick"/> после <see cref="TickFinanceStep"/>, до расчёта производства — чтобы
/// закупленное сырьё успело попасть в этот же расчёт производства (SPEC §4). Возвращает готовые
/// события, не применяет их.
/// </summary>
public static class EmergencyPurchaseStep
{
    public static IReadOnlyList<Change<GameSessionState>> Run(
        Team team, IReadOnlyDictionary<string, decimal> materialCosts, EconomyConfig economy,
        IReadOnlyList<EventLogEntry<GameSessionState>> entries, int currentTurn, Market market)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(materialCosts);
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(market);

        var changes = new List<Change<GameSessionState>>();

        foreach (var (materialId, volume) in team.PendingEmergencyPurchaseVolumeByMaterial.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var recentVolume = EmergencyPurchasePressureCalculator.CalculateRecentVolume(entries, team.Id, materialId, currentTurn, economy);
            var effectiveMultiplier = economy.EmergencyPurchaseBaseMultiplier
                + economy.EmergencyPurchasePressureMultiplierPerUnit * recentVolume;
            // Под внешней моделью базой служит котировка рынка (она уже несёт индекс), под
            // cost-plus — себестоимость материала. Надбавка за давление одна и та же.
            var priceBase = economy.PricingModel == PricingModel.External
                ? market.QuoteOf(materialId).Price
                : materialCosts.TryGetValue(materialId, out var cost) ? cost : 0m;
            var unitPrice = priceBase * effectiveMultiplier;

            changes.Add(new EmergencyPurchased
            {
                Id = Ulid.NewUlid(),
                TeamId = team.Id,
                MaterialId = materialId,
                Volume = volume,
                UnitPrice = unitPrice,
                TotalCost = unitPrice * volume,
                Turn = currentTurn,
            });
        }

        return changes;
    }
}
