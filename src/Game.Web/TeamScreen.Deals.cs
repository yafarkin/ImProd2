using Game.Domain;
using Game.Engine;

namespace Game.Web;

/// <summary>
/// Данные раздела «Сделки» и доски потребностей (docs/manager-ui/README.md §4, блок 4): с чем идти в
/// зал, репутация команд, доска по материалам и статистика поставок по контрактам. Раньше это жило в
/// трёх местах — вкладка «Контракты», <c>/team/negotiate</c> и <c>/team/needs</c>.
/// </summary>
public sealed partial class TeamScreen
{
    /// <summary>
    /// Материал, которого нашим фабрикам не хватит на ход при полной загрузке: <see cref="PerTurn"/> —
    /// сколько они заберут за ход, <see cref="OnStock"/> — сколько есть. Оценка «сверху», как и прогноз
    /// поставок в «Требует внимания»: реальная загрузка может быть ниже.
    /// </summary>
    public sealed record MaterialShortfall(string MaterialId, string MaterialName, decimal PerTurn, decimal OnStock);

    /// <summary>Репутация команды для переговоров: процент исполненных поставок и сколько фактов за ним стоит.</summary>
    public sealed record TeamReputation(Ulid TeamId, string TeamName, bool IsUs, decimal Percentage, int SampleCount);

    /// <summary>Поставки по контракту из журнала: сколько состоялось и сколько сорвано.</summary>
    public sealed record DeliveryStats(int Delivered, int Missed);

    /// <summary>Сообщение об успешном действии — «заявка подана», «черновик возвращён»; оболочка показывает его над разделом.</summary>
    public string? InfoMessage { get; set; }

    /// <summary>Чего нашим фабрикам не хватит на ход при полной загрузке («Нужно нам»).</summary>
    public IReadOnlyList<MaterialShortfall> Shortfalls { get; private set; } = [];

    /// <summary>«С чем идти в зал» переговорщика: свободный остаток, себестоимость и котировка по каждому материалу (<see cref="NegotiationBrief"/>).</summary>
    public IReadOnlyList<NegotiationBrief.Offer> Offers { get; private set; } = [];

    /// <summary>Сколько черновиков вошедшего переговорщика сейчас ждут управляющего — для его шапки.</summary>
    public int MyDraftsAwaitingManager { get; private set; }

    /// <summary>Репутация нашей и остальных команд — мы первыми, остальные по имени.</summary>
    public IReadOnlyList<TeamReputation> Reputations { get; private set; } = [];

    /// <summary>Доска потребностей зала, сгруппированная по материалу (<see cref="NeedsBoard"/>).</summary>
    public IReadOnlyList<NeedsBoard.MaterialGroup> Board { get; private set; } = [];

    /// <summary>Плата за одностороннее расторжение контракта (конфиг) — карточка называет её до нажатия.</summary>
    public decimal VoluntaryTerminationFee { get; private set; }

    /// <summary>Все материалы конфига по имени — для формы новой записи доски.</summary>
    public IReadOnlyList<MaterialOption> AllMaterials { get; private set; } = [];

    /// <summary>Поставки по каждому контракту команды; нет ключа — поставок ещё не было.</summary>
    public IReadOnlyDictionary<Ulid, DeliveryStats> DeliveriesByContractId { get; private set; } = new Dictionary<Ulid, DeliveryStats>();

    private int _deliveriesEntryCount = -1;

    private void RefreshDealsSection(GameSessionState state, Team team)
    {
        var maxOutputById = Factories.ToDictionary(f => f.FactoryId, f => f.CapacityBreakdown.TheoreticalMaxOutput);
        var consumption = team.Factories
            .SelectMany(factory => factory.SelectedRecipe.Inputs.Select(input => (
                input.Material,
                PerTurn: maxOutputById.GetValueOrDefault(factory.Id) / factory.SelectedRecipe.OutputQuantity * input.Quantity)))
            .GroupBy(x => x.Material.Id)
            .Select(group => (Material: group.First().Material, PerTurn: group.Sum(x => x.PerTurn)))
            .ToList();
        Shortfalls = consumption
            .Select(x => new MaterialShortfall(x.Material.Id, x.Material.Name, x.PerTurn, team.Warehouse.QuantityOf(x.Material)))
            .Where(shortfall => shortfall.PerTurn > shortfall.OnStock)
            .OrderBy(shortfall => shortfall.MaterialName, StringComparer.Ordinal)
            .ToList();

        Offers = NegotiationBrief.Build(
            team.Warehouse.Stock.Select(s => (s.Material, s.Quantity, UnitCostForNegotiation(s.Material.Id))).ToList(),
            consumption.ToDictionary(x => x.Material.Id, x => x.PerTurn, StringComparer.Ordinal),
            NegotiationBrief.Promised(state.Contracts.Values, TeamId, state.CurrentTurn),
            team.Warehouse.Stock
                .Where(s => state.Market.HasQuote(s.Material.Id))
                .ToDictionary(s => s.Material.Id, s => state.Market.QuoteOf(s.Material.Id).Price, StringComparer.Ordinal));

        MyDraftsAwaitingManager = Role == ParticipantRole.Negotiator && ParticipantCode is not null
            ? state.ContractDrafts.Values.Count(d => d.TeamId == TeamId
                && d.PreparedByParticipantCode == ParticipantCode
                && d.Status == ContractDraftStatus.AwaitingManager)
            : 0;

        Reputations = state.Teams.Values
            .Select(t =>
            {
                var reputation = Host.Session!.GetReputation(t.Id);
                return new TeamReputation(t.Id, t.Name, t.Id == TeamId, reputation.Percentage, reputation.SampleCount);
            })
            .OrderByDescending(r => r.IsUs)
            .ThenBy(r => r.TeamName, StringComparer.Ordinal)
            .ToList();

        var neededByUs = team.Factories
            .SelectMany(f => f.SelectedRecipe.Inputs.Select(input => input.Material.Id))
            .ToHashSet(StringComparer.Ordinal);
        Board = NeedsBoard.Build(
            state.Needs.Values.Where(n => n.Status == NeedStatus.Active).ToList(),
            state.Teams.Values.ToDictionary(t => t.Id, t => t.Name),
            TeamId,
            TeamWarehouseByMaterialId,
            neededByUs);

        VoluntaryTerminationFee = state.Config.Raw.Contracts.VoluntaryTerminationFee;

        AllMaterials = state.Config.Materials.Values
            .Select(m => new MaterialOption(m.Id, m.Name))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        // Проход по журналу — только когда в нём появились записи: страница обновляется раз в секунду.
        var entries = Host.Session!.Entries;
        if (_deliveriesEntryCount != entries.Count)
        {
            DeliveriesByContractId = CountDeliveries(entries);
            _deliveriesEntryCount = entries.Count;
        }
    }

    /// <summary>
    /// Себестоимость единицы для переговоров: реальная (средняя фактических трат на остаток), а если
    /// материал ещё не производился — оценка по рецепту; <c>null</c> — нет ни той, ни другой.
    /// </summary>
    private decimal? UnitCostForNegotiation(string materialId) =>
        RealUnitCostByMaterialId.GetValueOrDefault(materialId) is var real && real > 0m
            ? real
            : UnitCostByMaterialId.TryGetValue(materialId, out var estimate) ? estimate : null;

    /// <summary>Поставки и срывы по контрактам — прямо из событий журнала, как их считает и репутация.</summary>
    public static IReadOnlyDictionary<Ulid, DeliveryStats> CountDeliveries(IReadOnlyList<EventLogEntry<GameSessionState>> entries)
    {
        var delivered = new Dictionary<Ulid, int>();
        var missed = new Dictionary<Ulid, int>();
        foreach (var entry in entries)
        {
            if (entry.Change is ContractDelivered done)
            {
                delivered[done.ContractId] = delivered.GetValueOrDefault(done.ContractId) + 1;
            }
            else if (entry.Change is DeliveryMissed miss)
            {
                missed[miss.ContractId] = missed.GetValueOrDefault(miss.ContractId) + 1;
            }
        }

        return delivered.Keys.Union(missed.Keys)
            .ToDictionary(id => id, id => new DeliveryStats(delivered.GetValueOrDefault(id), missed.GetValueOrDefault(id)));
    }

    /// <summary>Подать черновик переговорщика заявкой как есть (SPEC §3).</summary>
    public void SubmitDraftAsIs(Ulid draftId) => RunAction(() =>
    {
        var draft = Host.Session!.State.ContractDrafts[draftId];
        var result = Host.Session.SubmitContractProposal(draft.Proposal, TeamRole.Manager, Random.Shared, draftId);
        InfoMessage = DescribeSubmission(result, out var mismatch);
        if (mismatch)
        {
            // Расхождение — не ошибка ввода: заявка сохранена, но игрок обязан это заметить.
            ErrorMessage = InfoMessage;
            InfoMessage = null;
        }
    });

    /// <summary>Вернуть черновик переговорщику с необязательной причиной.</summary>
    public void ReturnDraft(Ulid draftId, string? reason) => RunAction(() =>
    {
        Host.Session!.ReturnContractDraft(draftId, TeamId, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
        InfoMessage = "Черновик возвращён переговорщику.";
    });

    /// <summary>Опубликовать запись на доске потребностей. Доска открыта в любой фазе — это объявление, а не решение хода.</summary>
    public void PostNeed(string materialId, NeedDirection direction, NeedVolumeOrder volumeOrder, string? comment) => RunAction(() =>
    {
        Host.Session!.PostNeed(TeamId, materialId, direction, volumeOrder, string.IsNullOrWhiteSpace(comment) ? null : comment.Trim());
        InfoMessage = "Запись опубликована — её видят все команды.";
    });

    /// <summary>Снять свою запись с доски.</summary>
    public void WithdrawNeed(Ulid needId) => RunAction(() => Host.Session!.WithdrawNeed(TeamId, needId));

    /// <summary>
    /// Что сказать после подачи заявки. Чужие числа не называем — только расходящиеся поля (№16):
    /// иначе сверка выродилась бы в копирование условий контрагента.
    /// </summary>
    public static string DescribeSubmission(ContractProposalSubmissionResult result, out bool mismatch)
    {
        ArgumentNullException.ThrowIfNull(result);

        mismatch = !result.IsMatched && result.HasCounterpartyProposal;
        if (result.IsMatched)
        {
            return "Условия сошлись — сделка заключена, ждёт подтверждения управляющего контрагента.";
        }

        return mismatch
            ? "Заявка от контрагента уже есть, но условия не сошлись: "
                + string.Join("; ", result.Mismatches.Select(DashboardDisplay.ContractMismatchLabel))
                + ". Заявка сохранена — расхождение уточняется лично, затем заявка подаётся заново."
            : "Заявка подана и ждёт встречной от контрагента.";
    }
}
