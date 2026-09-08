using Game.Domain;

namespace Game.Engine;

/// <summary>
/// «Требует внимания» (docs/TODO.md №7) — что у команды уже идёт не так прямо сейчас и что сломается
/// в ближайшие <see cref="SoonHorizonTurns"/> ходов, если ничего не делать. Считается по тем же
/// формулам, по которым работает движок, и на тех же данных, что уже показывает <c>/team</c>: своей
/// механики здесь нет ни одной, это подача уже существующих фактов.
///
/// <para>
/// <b>Граница, за которую этот класс не заходит:</b> он сообщает <i>что происходит и почему</i>, но
/// никогда — <i>что делать</i>. Никаких «постройте фабрику X», «закупите Y единиц», «продайте по
/// цене Z», никакого ранжирования возможностей и никаких рыночных прогнозов (для рынка есть лента
/// новостей, она и так предупреждает за <c>NewsLookaheadTurns</c> ходов). Причина в назначении:
/// участники играют первый и часто единственный раз, и панель обязана спасти их от провала на самых
/// основах — но не сыграть партию за них. Если сигнал невозможно сформулировать как факт с причиной,
/// ему здесь не место.
/// </para>
///
/// <para>
/// <b>Смещение в сторону молчания.</b> Все прогнозы («скоро») считаются по ОПТИМИСТИЧНОЙ оценке
/// собственного выпуска — по потолку мощности (<see cref="ProductionCalculator.CalculateCapacityBreakdown"/>),
/// как если бы сырья хватало всегда. Поэтому предупреждение появляется только тогда, когда беда
/// неизбежна даже при идеальной загрузке, и не появляется в спорных случаях. Панель, которая кричит
/// зря, перестаёт читаться на третьем ходу — а её адресат тот, кто ещё не умеет отличать ложную
/// тревогу от настоящей.
/// </para>
///
/// <para>
/// <b>«Ближайший расчёт», а не «текущий ход».</b> Порядок фаз внутри хода — расчёт, потом решения
/// (<see cref="PhaseAdvanced"/>), поэтому в фазу решений хода N расчёт этого хода УЖЕ прошёл, и
/// ближайшее, на что игрок ещё может повлиять, — расчёт хода N+1. Все сроки здесь считаются от этого
/// «ближайшего расчёта» (<see cref="UpcomingSettlementTurn"/>), иначе панель предупреждала бы о
/// поставке ходом позже, чем нужно, — то есть ровно тогда, когда штраф уже списан.
/// </para>
/// </summary>
public static class TeamAttentionCalculator
{
    /// <summary>
    /// Горизонт «выстрелит через несколько ходов». Три хода — примерно 5-12 минут реального времени
    /// при ходе 1.8-4 минуты: успеть среагировать можно, а панель не забита далёким будущим.
    /// </summary>
    public const int SoonHorizonTurns = 3;

    /// <summary>
    /// Со скольких ходов подряд простой считается поводом для сигнала. Единичный недобор мощности уже
    /// виден на самой карточке фабрики красной рамкой (<c>FactoryOverviewList.LoadStatus</c>) —
    /// дублировать его в панели незачем; смысл сигнала именно в том, что беда ДЛИТСЯ (реальный
    /// случай из разбора лога 2026-08-09: фабрика простояла 25 ходов подряд, и на 25-м ходу экран
    /// выглядел ровно так же, как на первом).
    /// </summary>
    public const int MinStarvedTurnsInARow = 2;

    /// <summary>
    /// Доля потолка мощности, ниже которой выпуск считается урезанным нехваткой сырья. Не строгое
    /// «меньше потолка»: <c>OutputQuantity</c> получается обратным умножением после деления на
    /// <c>OutputQuantity</c> рецепта (<see cref="ProductionCalculator.CalculateGroup"/>), поэтому при
    /// полной загрузке может отличаться от потолка на исчезающе малую величину округления decimal.
    /// Полпроцента запаса заведомо перекрывает округление и заведомо меньше любого дефицита, который
    /// стоит показывать человеку.
    /// </summary>
    private const decimal FullLoadTolerance = 0.995m;

    /// <summary>Насколько срочен сигнал: «уже происходит» против «сломается через несколько ходов».</summary>
    public enum AttentionHorizon
    {
        /// <summary>Деньги теряются уже сейчас либо потеря случится в ближайшем расчёте.</summary>
        Now,

        /// <summary>Сломается в пределах <see cref="SoonHorizonTurns"/> ходов, если ничего не менять.</summary>
        Soon,
    }

    /// <summary>
    /// Один повод обратить внимание. Наследники — закрытый список поводов; текст для человека
    /// собирает интерфейс (<c>Game.Web</c>), здесь только факты и числа — тот же принцип, что у
    /// <see cref="FinanceHistoryCalculator.OperationType"/> и его подписи в <c>DashboardDisplay</c>.
    /// </summary>
    public abstract record AttentionItem(AttentionHorizon Horizon)
    {
        /// <summary>Фабрика <paramref name="TurnsInARow"/> ходов подряд не выходит на потолок мощности, и прямо сейчас ей не хватает <paramref name="MaterialId"/> на складе команды.</summary>
        public sealed record FactoryStarvedOfInput(Ulid FactoryId, string MaterialId, int TurnsInARow, decimal ShortfallPerTurn)
            : AttentionItem(AttentionHorizon.Now);

        /// <summary>Фабрика построена, содержание за неё платится каждый ход, но рабочих на ней нет и наём не объявлен — выпуска не будет вообще.</summary>
        public sealed record FactoryWithoutWorkers(Ulid FactoryId) : AttentionItem(AttentionHorizon.Now);

        /// <summary>
        /// Фабрика на ВЫНУЖДЕННОМ простое по износу (<see cref="Config.Economy.WearConfig.CriticalConditionThreshold"/>).
        /// Добровольный капремонт сюда не попадает намеренно: его команда заказала сама, это её решение, а не беда.
        /// </summary>
        public sealed record FactoryInForcedDowntime(Ulid FactoryId, int TurnsRemaining) : AttentionItem(AttentionHorizon.Now);

        /// <summary>Поставка по контракту идёт в ближайшем расчёте, и её не хватит даже при полной загрузке своих фабрик: не хватает <paramref name="Shortfall"/>, штраф составит <paramref name="Penalty"/>.</summary>
        public sealed record DeliveryDueAndShort(Ulid ContractId, string MaterialId, decimal Shortfall, decimal Penalty)
            : AttentionItem(AttentionHorizon.Now);

        /// <summary>Склад вышел за бесплатный лимит, и за это каждый ход списывается <paramref name="FeePerTurn"/>.</summary>
        public sealed record WarehouseOverFreeCapacity(decimal OverageQuantity, decimal FeePerTurn) : AttentionItem(AttentionHorizon.Now);

        /// <summary>Через <paramref name="TurnsUntil"/> ходов состояние фабрики упадёт настолько, что сработает более дорогая ступень капремонта — чинить станет дороже и дольше.</summary>
        public sealed record OverhaulGetsMoreExpensive(Ulid FactoryId, int TurnsUntil, string CurrentTierId, string NextTierId)
            : AttentionItem(AttentionHorizon.Soon);

        /// <summary>Поставка по контракту через <paramref name="TurnsUntil"/> ходов, и к тому моменту не хватит <paramref name="ProjectedShortfall"/> даже при полной загрузке своих фабрик.</summary>
        public sealed record DeliveryAheadWillBeShort(Ulid ContractId, string MaterialId, int TurnsUntil, decimal ProjectedShortfall)
            : AttentionItem(AttentionHorizon.Soon);

        /// <summary>Материал расходуется быстрее, чем производится: остатка хватит ещё на <paramref name="TurnsUntil"/> ходов, потом встанут фабрики <paramref name="AffectedFactoryIds"/>.</summary>
        public sealed record MaterialRunningOut(string MaterialId, int TurnsUntil, IReadOnlyList<Ulid> AffectedFactoryIds)
            : AttentionItem(AttentionHorizon.Soon);
    }

    /// <summary>
    /// Ход ближайшего расчёта — тот, на который игрок ещё может повлиять решениями (см. doc-comment
    /// класса про порядок фаз). В фазу решений это следующий ход, в фазу расчёта — текущий.
    /// </summary>
    public static int UpcomingSettlementTurn(GameSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.CurrentPhase == TurnPhase.Decision ? state.CurrentTurn + 1 : state.CurrentTurn;
    }

    /// <summary>
    /// Все поводы обратить внимание для одной команды, в порядке подачи: сначала «уже сейчас» (внутри
    /// — от того, что стоит дороже всего), затем «через несколько ходов» по близости срока. Пустой
    /// список — нормальное и частое состояние, интерфейс обязан показывать его явно, иначе панель
    /// превращается в фон.
    /// </summary>
    public static IReadOnlyList<AttentionItem> Calculate(
        IReadOnlyList<EventLogEntry<GameSessionState>> entries, GameSessionState state, Ulid teamId)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(state);

        if (!state.Teams.TryGetValue(teamId, out var team))
        {
            return Array.Empty<AttentionItem>();
        }

        var config = state.Config;
        var upcomingTurn = UpcomingSettlementTurn(state);
        var starvedStreaks = CountStarvedStreaks(entries, teamId);
        // По ОБЪЯВЛЕННОЙ численности, а не по вчерашней фактической: наём применяется расчётом
        // раньше производства того же хода (WorkforceStep внутри TickFinanceStep), поэтому мощность
        // ближайшего расчёта определяет именно она. Иначе только что построенная фабрика с уже
        // объявленной бригадой считалась бы нулевой — ни сырья не просит, ни выпуска не даёт.
        var capacityOutput = team.Factories.ToDictionary(
            factory => factory.Id,
            factory => ProductionCalculator
                .CalculateCapacityBreakdown(factory, factory.DesiredWorkers, config.Raw.WorkerProductivity, config.Raw.Rnd)
                .TheoreticalMaxOutput);

        var now = new List<AttentionItem>();
        var soon = new List<AttentionItem>();

        foreach (var factory in team.Factories.OrderBy(f => f.Id))
        {
            AddFactoryItems(factory, team, config, starvedStreaks, capacityOutput, upcomingTurn, now, soon);
        }

        AddWarehouseFee(team, config, now);
        AddContractItems(state, team, config, capacityOutput, upcomingTurn, now, soon);
        AddMaterialsRunningOut(team, config, capacityOutput, starvedStreaks, soon);

        return now.Concat(soon.OrderBy(TurnsUntilOf)).ToList();
    }

    private static int TurnsUntilOf(AttentionItem item) => item switch
    {
        AttentionItem.OverhaulGetsMoreExpensive x => x.TurnsUntil,
        AttentionItem.DeliveryAheadWillBeShort x => x.TurnsUntil,
        AttentionItem.MaterialRunningOut x => x.TurnsUntil,
        _ => 0,
    };

    /// <summary>
    /// Сколько ходов подряд, считая назад от последнего, фабрика не выходила на потолок мощности.
    /// Считается по журналу, а не по остаткам: <see cref="FactoryProduced"/> несёт и фактический
    /// выпуск, и потолок того же хода (<see cref="FactoryProduced.CapacityLimitedOutputQuantity"/>),
    /// и первый строго меньше второго тогда и только тогда, когда выпуск урезало сырьё — износ и
    /// простой в потолок уже заложены (<see cref="ProductionCalculator.CalculateGroup"/>), так что с
    /// ними этот сигнал не путается.
    /// </summary>
    private static Dictionary<Ulid, int> CountStarvedStreaks(
        IReadOnlyList<EventLogEntry<GameSessionState>> entries, Ulid teamId)
    {
        var streaks = new Dictionary<Ulid, int>();
        foreach (var entry in entries)
        {
            if (entry.Change is not FactoryProduced produced || produced.TeamId != teamId)
            {
                continue;
            }

            var starved = produced.OutputQuantity < produced.CapacityLimitedOutputQuantity * FullLoadTolerance;
            streaks[produced.FactoryId] = starved ? streaks.GetValueOrDefault(produced.FactoryId) + 1 : 0;
        }

        return streaks;
    }

    private static void AddFactoryItems(
        Factory factory, Team team, Config.Loading.ResolvedGameConfig config,
        IReadOnlyDictionary<Ulid, int> starvedStreaks, IReadOnlyDictionary<Ulid, decimal> capacityOutput,
        int upcomingTurn, List<AttentionItem> now, List<AttentionItem> soon)
    {
        // Вынужденный простой отличается от добровольного капремонта целью восстановления: любая
        // ступень чинит до 1.0, safety net движка — до PostForcedRepairCondition, то есть строго ниже
        // (см. WearConfig.PostForcedRepairCondition).
        if (factory.IsUnderRepair)
        {
            if (factory.RepairTargetCondition < 1m)
            {
                now.Add(new AttentionItem.FactoryInForcedDowntime(factory.Id, factory.RepairTurnsRemaining));
            }

            return;
        }

        if (factory is { Workers: 0, DesiredWorkers: 0 })
        {
            now.Add(new AttentionItem.FactoryWithoutWorkers(factory.Id));
            return;
        }

        AddStarvationItem(factory, team, config, starvedStreaks, capacityOutput, now);
        AddOverhaulItem(factory, config, upcomingTurn, soon);
    }

    /// <summary>
    /// Простой по нехватке сырья. Требует ОБОИХ условий сразу: беда длится (<see
    /// cref="MinStarvedTurnsInARow"/> ходов по журналу) И сырья не хватает прямо сейчас. Второе
    /// условие обязательно, иначе панель показывала бы уже разрешившуюся проблему — и заодно оно
    /// отвечает на вопрос «какого именно материала не хватает», не угадывая его по истории.
    /// </summary>
    private static void AddStarvationItem(
        Factory factory, Team team, Config.Loading.ResolvedGameConfig config,
        IReadOnlyDictionary<Ulid, int> starvedStreaks, IReadOnlyDictionary<Ulid, decimal> capacityOutput,
        List<AttentionItem> now)
    {
        var streak = starvedStreaks.GetValueOrDefault(factory.Id);
        if (streak < MinStarvedTurnsInARow)
        {
            return;
        }

        var batches = capacityOutput[factory.Id] / factory.SelectedRecipe.OutputQuantity;
        var worstShortfall = 0m;
        RecipeInput? worstInput = null;
        foreach (var input in factory.SelectedRecipe.Inputs)
        {
            var shortfall = batches * input.Quantity - team.Warehouse.QuantityOf(input.Material);
            if (shortfall > worstShortfall)
            {
                worstShortfall = shortfall;
                worstInput = input;
            }
        }

        if (worstInput is not null)
        {
            now.Add(new AttentionItem.FactoryStarvedOfInput(factory.Id, worstInput.Material.Id, streak, worstShortfall));
        }
    }

    /// <summary>
    /// Подорожание капремонта. Состояние прокручивается вперёд теми же функциями, что применит
    /// движок (<see cref="WearCalculator"/>), и сигнал появляется на ходу, где сработала бы уже
    /// другая, более тяжёлая ступень. Отдельного сигнала «фабрика встанет принудительно» нет
    /// намеренно: до критического порога состояние идёт через все промежуточные ступени, каждая из
    /// которых предупредит заранее, — а лишний повод в панели стоит дороже, чем кажется.
    /// </summary>
    private static void AddOverhaulItem(
        Factory factory, Config.Loading.ResolvedGameConfig config, int upcomingTurn, List<AttentionItem> soon)
    {
        if (factory.OverhaulRequested)
        {
            return;
        }

        var wear = config.Raw.Wear;
        var currentTier = WearCalculator.SelectTier(factory.Condition, wear.OverhaulTiers);
        if (currentTier is null)
        {
            return;
        }

        var condition = factory.Condition;
        for (var offset = 0; offset < SoonHorizonTurns; offset++)
        {
            var turn = upcomingTurn + offset;
            var age = WearCalculator.CalculateAgeBeyondGrace(factory.LastResetTurn, turn, wear.GracePeriodTurns);
            condition = WearCalculator.CalculateNextCondition(condition, WearCalculator.CalculateDecayRate(age, wear));

            var tier = WearCalculator.SelectTier(condition, wear.OverhaulTiers);
            if (tier is not null && tier.Id != currentTier.Id)
            {
                soon.Add(new AttentionItem.OverhaulGetsMoreExpensive(factory.Id, offset + 1, currentTier.Id, tier.Id));
                return;
            }
        }
    }

    private static void AddWarehouseFee(Team team, Config.Loading.ResolvedGameConfig config, List<AttentionItem> now)
    {
        var totalStock = team.Warehouse.Stock.Sum(stock => stock.Quantity);
        var fee = WarehouseFeeCalculator.Calculate(totalStock, config.Raw.Warehouse);
        if (fee.Fee > 0m)
        {
            now.Add(new AttentionItem.WarehouseOverFreeCapacity(fee.OverageQuantity, fee.Fee));
        }
    }

    /// <summary>
    /// Поставки, которые команда не потянет. Считается только там, где команда — ПРОДАВЕЦ: сорванная
    /// поставка контрагента — не её решение и не её действие.
    ///
    /// <para>
    /// Прогноз остатка на момент поставки — «остаток сейчас + потолок собственного выпуска этого
    /// материала за оставшиеся ходы». Оптимистично сразу с двух сторон (выпуск берётся по потолку
    /// мощности, а объявленные продажи системе и расход соседних поставок не вычитаются), и это
    /// сознательно: сигнал появляется только там, где нехватка неизбежна. Собственные фабрики,
    /// которые едят этот материал, из расчёта не вычитаются по существу механики, а не для простоты —
    /// поставка уровня L исполняется в тике сразу после производства уровня L, то есть РАНЬШЕ, чем
    /// потребители уровня L+1 успевают что-либо забрать (<see cref="GameSession.RunTick"/>).
    /// </para>
    /// </summary>
    private static void AddContractItems(
        GameSessionState state, Team team, Config.Loading.ResolvedGameConfig config,
        IReadOnlyDictionary<Ulid, decimal> capacityOutput, int upcomingTurn,
        List<AttentionItem> now, List<AttentionItem> soon)
    {
        var outputPerTurnByMaterialId = team.Factories
            .GroupBy(factory => factory.SelectedRecipe.Output.Id)
            .ToDictionary(group => group.Key, group => group.Sum(factory => capacityOutput[factory.Id]));

        foreach (var contract in state.Contracts.Values.OrderBy(contract => contract.Id))
        {
            if (contract.SellerTeamId != team.Id)
            {
                continue;
            }

            for (var offset = 0; offset < SoonHorizonTurns; offset++)
            {
                var turn = upcomingTurn + offset;
                if (!ContractExecution.IsDeliveryDue(contract, turn))
                {
                    continue;
                }

                var terms = contract.Terms;
                var projected = team.Warehouse.QuantityOf(terms.Material)
                                + outputPerTurnByMaterialId.GetValueOrDefault(terms.Material.Id) * (offset + 1);
                var shortfall = terms.Volume - projected;
                if (shortfall <= 0m)
                {
                    break;
                }

                if (offset == 0)
                {
                    now.Add(new AttentionItem.DeliveryDueAndShort(
                        contract.Id, terms.Material.Id, shortfall, terms.Volume * terms.UnitPrice * terms.PenaltyRate));
                }
                else
                {
                    soon.Add(new AttentionItem.DeliveryAheadWillBeShort(contract.Id, terms.Material.Id, offset, shortfall));
                }

                // Про один и тот же контракт хватит одного, самого раннего повода: у регулярного
                // контракта поставка положена каждый ход диапазона, и без этого он занял бы всю панель.
                break;
            }
        }
    }

    /// <summary>
    /// Материал, который команда тратит быстрее, чем производит. Уже кончившийся материал сюда не
    /// попадает — это забота <see cref="AttentionItem.FactoryStarvedOfInput"/>, иначе одна и та же
    /// беда пришла бы в панель дважды, из «сейчас» и из «скоро».
    /// </summary>
    private static void AddMaterialsRunningOut(
        Team team, Config.Loading.ResolvedGameConfig config, IReadOnlyDictionary<Ulid, decimal> capacityOutput,
        IReadOnlyDictionary<Ulid, int> starvedStreaks, List<AttentionItem> soon)
    {
        var consumers = new Dictionary<string, List<Ulid>>(StringComparer.Ordinal);
        var consumptionPerTurn = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var productionPerTurn = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var factory in team.Factories)
        {
            var output = capacityOutput[factory.Id];
            productionPerTurn[factory.SelectedRecipe.Output.Id] =
                productionPerTurn.GetValueOrDefault(factory.SelectedRecipe.Output.Id) + output;

            var batches = output / factory.SelectedRecipe.OutputQuantity;
            foreach (var input in factory.SelectedRecipe.Inputs)
            {
                consumptionPerTurn[input.Material.Id] = consumptionPerTurn.GetValueOrDefault(input.Material.Id) + batches * input.Quantity;
                if (!consumers.TryGetValue(input.Material.Id, out var list))
                {
                    list = [];
                    consumers[input.Material.Id] = list;
                }

                list.Add(factory.Id);
            }
        }

        foreach (var (materialId, consumed) in consumptionPerTurn.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var deficitPerTurn = consumed - productionPerTurn.GetValueOrDefault(materialId);
            if (deficitPerTurn <= 0m)
            {
                continue;
            }

            var stock = team.Warehouse.QuantityOf(config.Materials[materialId]);
            var turnsUntil = (int)Math.Floor(stock / deficitPerTurn);
            if (turnsUntil <= 0 || turnsUntil > SoonHorizonTurns)
            {
                continue;
            }

            var affected = consumers[materialId]
                .Where(id => starvedStreaks.GetValueOrDefault(id) < MinStarvedTurnsInARow)
                .OrderBy(id => id)
                .ToList();
            if (affected.Count > 0)
            {
                soon.Add(new AttentionItem.MaterialRunningOut(materialId, turnsUntil, affected));
            }
        }
    }
}
