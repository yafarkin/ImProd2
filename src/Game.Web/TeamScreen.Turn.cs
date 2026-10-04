using Game.Domain;
using Game.Engine;

namespace Game.Web;

/// <summary>
/// Данные раздела «Ход» (docs/manager-ui/README.md §4, блок 2): что принёс последний расчёт, что ждёт
/// решения управляющего и что уже решено на этот ход. Всё пересчитывается вместе с остальной моделью
/// в <see cref="Refresh"/>.
/// </summary>
public sealed partial class TeamScreen
{
    /// <summary>Что именно ждёт решения управляющего — от этого зависят кнопки на карточке.</summary>
    public enum AwaitingKind
    {
        /// <summary>Черновик переговорщика (SPEC §3).</summary>
        Draft,

        /// <summary>Контракт, который сведён и ждёт нашего подтверждения.</summary>
        Confirmation,

        /// <summary>Контрагент предложил пересмотреть действующий регулярный контракт.</summary>
        Revision,

        /// <summary>Контрагент подал заявку на сделку с нами; условия он назовёт лично.</summary>
        IncomingProposal,
    }

    /// <summary>Одна карточка «Ждёт моего решения»: <see cref="Id"/> — черновик, контракт или заявка, смотря по <see cref="Kind"/>.</summary>
    public sealed record AwaitingItem(AwaitingKind Kind, Ulid Id, string Title, string Detail);

    /// <summary>
    /// Строка «Приказа на ход» — одно решение, которое уйдёт в расчёт. <see cref="Amount"/> со знаком:
    /// плюс — деньги придут, минус — уйдут. <see cref="EveryTurn"/> — постоянное вложение, которое
    /// списывается каждый ход, пока его не изменить. <see cref="Cancel"/> — отмена одним нажатием, если
    /// она есть; <see cref="EditHref"/> — где поменять значение.
    /// </summary>
    public sealed record OrderLine(string Text, decimal Amount, bool EveryTurn, Action? Cancel, string? EditHref);

    /// <summary>Итоги последнего расчёта с денежными операциями; <see langword="null"/> — расчётов ещё не было.</summary>
    public SettlementSummaryDisplay.Summary? LastSettlement { get; private set; }

    /// <summary>Что произвели фабрики в том же расчёте, по материалам.</summary>
    public IReadOnlyList<(string MaterialName, decimal Quantity)> LastSettlementOutput { get; private set; } = [];

    /// <summary>Всё, что ждёт решения управляющего: черновики, подтверждения, пересмотры, входящие заявки.</summary>
    public IReadOnlyList<AwaitingItem> Awaiting { get; private set; } = [];

    /// <summary>Все решения этого хода, которые уйдут в расчёт (SPEC §4: решение — намерение, исполняется при расчёте).</summary>
    public IReadOnlyList<OrderLine> OrderLines { get; private set; } = [];

    private void RefreshTurnSection(GameSessionState state, Team team)
    {
        LastSettlement = SettlementSummaryDisplay.Build(FinanceHistory);
        LastSettlementOutput = LastSettlement is { } summary
            ? Factories
                .SelectMany(f => f.ActivityHistory.Where(row => row.Turn == summary.Turn && row.OutputQuantity > 0)
                    .Select(row => (Material: f.ProducedMaterial.Name, row.OutputQuantity)))
                .GroupBy(x => x.Material)
                .Select(g => (g.Key, g.Sum(x => x.OutputQuantity)))
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToList()
            : [];

        Awaiting = BuildAwaiting(state);
        AwaitingManagerCount = Role == ParticipantRole.Manager ? Awaiting.Count : 0;
        OrderLines = state.IsFinished ? [] : BuildOrderLines(state, team);
    }

    private List<AwaitingItem> BuildAwaiting(GameSessionState state)
    {
        var items = new List<AwaitingItem>();

        foreach (var draft in state.ContractDrafts.Values
            .Where(d => d.TeamId == TeamId && d.Status == ContractDraftStatus.AwaitingManager)
            .OrderBy(d => d.Id.ToString(), StringComparer.Ordinal))
        {
            var proposal = draft.Proposal;
            var weAreBuyer = proposal.BuyerTeamId == TeamId;
            var counterpartyId = weAreBuyer ? proposal.SellerTeamId : proposal.BuyerTeamId;
            // Имя не склоняется («Черновик от Петя» — живой обход 2026-10-04), поэтому оно стоит
            // приложением к «переговорщика», а не после «от».
            var author = state.Participants.TryGetValue(draft.PreparedByParticipantCode, out var participant)
                ? $"Черновик переговорщика {participant.DisplayName}"
                : "Черновик переговорщика";
            items.Add(new AwaitingItem(
                AwaitingKind.Draft, draft.Id,
                $"{author}: {(weAreBuyer ? "покупка у команды" : "продажа команде")} «{TeamNameOf(state, counterpartyId)}», {proposal.Terms.Material.Name}",
                DashboardDisplay.DraftTermsLabel(proposal.Terms)));
        }

        foreach (var contract in Contracts.Where(c => c.Status == ContractStatus.PendingConfirmation && !c.WeAreTheProposer))
        {
            items.Add(new AwaitingItem(
                AwaitingKind.Confirmation, contract.ContractId,
                $"Контракт с командой «{contract.CounterpartyName}» ждёт вашего подтверждения: {contract.MaterialName}",
                $"{(contract.Role == "Покупатель" ? "покупаем" : "продаём")} {contract.Volume:0.##} ед. × {DashboardDisplay.FormatUnitCost(contract.UnitPrice)}"
                + $" · {DashboardDisplay.ContractTypeLabel(contract.Type).ToLowerInvariant()} · штраф {DashboardDisplay.FormatRate(contract.PenaltyRate)}"));
        }

        foreach (var contract in Contracts.Where(c => c.HasPendingRevision && !c.PendingRevisionIsMine))
        {
            items.Add(new AwaitingItem(
                AwaitingKind.Revision, contract.ContractId,
                $"Команда «{contract.CounterpartyName}» предлагает пересмотр контракта: {contract.MaterialName}",
                $"объём {contract.Volume:0.##} → {contract.PendingVolume:0.##}"
                + $" · цена {DashboardDisplay.FormatUnitCost(contract.UnitPrice)} → {DashboardDisplay.FormatUnitCost(contract.PendingUnitPrice ?? 0m)}"
                + $" · штраф {DashboardDisplay.FormatRate(contract.PendingPenaltyRate ?? 0m)}"
                + (contract.PendingRecurringEndTurn is { } endTurn ? $" · до хода {endTurn}" : " · бессрочно")));
        }

        foreach (var proposal in Proposals.Where(p => p.Incoming))
        {
            items.Add(new AwaitingItem(
                AwaitingKind.IncomingProposal, proposal.ProposalId,
                $"Команда «{proposal.CounterpartyName}» подала заявку на сделку: {proposal.MaterialName}",
                "Условия скрыты — их назовут лично. Совпадут с вашей встречной заявкой — сделка заключится сама."));
        }

        return items;
    }

    private List<OrderLine> BuildOrderLines(GameSessionState state, Team team)
    {
        var lines = new List<OrderLine>();

        foreach (var factory in Factories.Where(f => f.DesiredWorkers != f.Workers))
        {
            var delta = factory.DesiredWorkers - factory.Workers;
            if (delta > 0)
            {
                var hiredNow = HiresThisTurn(factory, delta);
                var text = hiredNow < delta
                    ? $"{factory.DefinitionName}: нанять {delta} — в этот расчёт выйдут {hiredNow}, остальные в следующие ходы"
                    : $"{factory.DefinitionName}: нанять {delta}";
                lines.Add(new OrderLine(text, -hiredNow * HireCostPerWorker, EveryTurn: false, () => CancelWorkerCount(factory), FactoryHref(factory.FactoryId, "workers")));
            }
            else
            {
                lines.Add(new OrderLine(
                    $"{factory.DefinitionName}: уволить {-delta}", delta * FireCostPerWorker, EveryTurn: false,
                    () => CancelWorkerCount(factory), FactoryHref(factory.FactoryId, "workers")));
            }
        }

        foreach (var factory in Factories.Where(f => f.OverhaulRequested && !f.IsUnderRepair))
        {
            lines.Add(new OrderLine(
                $"{factory.DefinitionName}: капремонт «{factory.OverhaulTierName}», простой {DashboardDisplay.Turns(factory.OverhaulTierDurationTurns)}",
                -factory.OverhaulTierCost, EveryTurn: false,
                () => CancelOverhaulRequest(factory.FactoryId), FactoryHref(factory.FactoryId, "wear")));
        }

        foreach (var (materialId, volume) in team.PendingSaleVolumeByMaterial.Where(p => p.Value > 0).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var material = state.Config.Materials[materialId];
            var revenue = Market is not null && EconomyConfig is not null && Host.Session is not null
                ? MarketSalePreview.Calculate(
                    Market, MaterialCosts, EconomyConfig, material, volume,
                    RealUnitCostByMaterialId.GetValueOrDefault(materialId), Host.Session.Entries, CurrentTurn).Revenue
                : 0m;
            lines.Add(new OrderLine(
                $"Продать системе: {material.Name}, {volume:0.##} ед.", revenue, EveryTurn: false,
                () => CancelSaleToSystem(materialId), "/team/production"));
        }

        foreach (var (materialId, volume) in team.PendingEmergencyPurchaseVolumeByMaterial.Where(p => p.Value > 0).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var material = state.Config.Materials[materialId];
            var cost = MaterialCosts.GetValueOrDefault(materialId) * EmergencyPurchaseMultiplierFor(materialId) * volume;
            lines.Add(new OrderLine(
                $"Аварийная закупка: {material.Name}, {volume:0.##} ед.", -cost, EveryTurn: false,
                () => CancelEmergencyPurchaseOf(materialId), "/team/production"));
        }

        foreach (var factory in Factories.Where(f => f.RndCommitmentPerTurn > 0 && f.NextLevelThreshold is not null))
        {
            lines.Add(new OrderLine(
                $"{factory.DefinitionName}: R&D фабрики", -factory.RndCommitmentPerTurn, EveryTurn: true,
                Cancel: null, FactoryHref(factory.FactoryId, "rnd")));
        }

        if (GenerationResearchCommitmentPerTurn > 0 && NextGenerationThreshold is not null)
        {
            lines.Add(new OrderLine(
                $"Исследование поколения {UnlockedGeneration + 1}", -GenerationResearchCommitmentPerTurn, EveryTurn: true,
                Cancel: null, "/team/production"));
        }

        return lines;
    }

    /// <summary>Снимает заявку на аварийную закупку конкретного материала — для «Приказа на ход», где материал не выбран в форме.</summary>
    public void CancelEmergencyPurchaseOf(string materialId) =>
        RunAction(() => Host.Session!.EmergencyPurchase(TeamId, materialId, 0m));

    private static string FactoryHref(Ulid factoryId, string tab) => $"/team/factory/{factoryId}?tab={tab}";

    private static string TeamNameOf(GameSessionState state, Ulid teamId) =>
        state.Teams.TryGetValue(teamId, out var team) ? team.Name : "?";
}
