using Game.Config.Economy;
using Game.Domain;
using Game.Engine;

namespace Game.Web;

/// <summary>
/// Форматирование дашборда команды (Блок 9.1, SPEC §9.3) — по тому же принципу, что и
/// <see cref="PhaseDisplay"/>: чистые статические функции над уже посчитанным состоянием сессии,
/// без собственного хранимого состояния.
/// </summary>
public static class DashboardDisplay
{
    /// <summary>Денежная сумма для отображения на экране — универсальный знак валюты (U+00A4), не привязанный к конкретной стране.</summary>
    public static string FormatMoney(decimal amount) => $"{amount:N0} ¤";

    /// <summary>
    /// Себестоимость/цена одной единицы товара — в отличие от <see cref="FormatMoney"/> (округление до
    /// целого, годится для сумм по партии/ходу), тут значение систематически на 2-4 порядка меньше
    /// (делится на объём выпуска), и округление до целого почти всегда даёт бесполезный «0 ¤»
    /// (обнаружено на живой проверке блока «Прибыльность»: себестоимость 0.03 показывалась как 0 ¤).
    /// </summary>
    public static string FormatUnitCost(decimal amount) => $"{amount:0.####} ¤";

    /// <summary>Процентная ставка для отображения на экране — штраф контракта, плата за склад и т.п.</summary>
    public static string FormatRate(decimal rate) => rate.ToString("P1");

    /// <summary>Русская подпись статуса контракта для дашборда («что я обещал другим»).</summary>
    public static string ContractStatusLabel(ContractStatus status) => status switch
    {
        ContractStatus.PendingConfirmation => "Ждёт подтверждения",
        ContractStatus.Active => "Действует",
        ContractStatus.Completed => "Исполнен",
        ContractStatus.Terminated => "Расторгнут",
        ContractStatus.Rejected => "Отклонён",
        _ => status.ToString()
    };

    /// <summary>Русская подпись направления записи доски потребностей (Блок 9.4).</summary>
    public static string NeedDirectionLabel(NeedDirection direction) => direction switch
    {
        NeedDirection.Surplus => "Излишек",
        NeedDirection.Deficit => "Дефицит",
        _ => direction.ToString()
    };

    /// <summary>Русская подпись грубого порядка объёма записи доски потребностей (Блок 9.4).</summary>
    public static string NeedVolumeOrderLabel(NeedVolumeOrder order) => order switch
    {
        NeedVolumeOrder.Small => "Небольшой",
        NeedVolumeOrder.Medium => "Средний",
        NeedVolumeOrder.Large => "Крупный",
        _ => order.ToString()
    };

    /// <summary>Русская подпись тренда экономики (Блок 9.6).</summary>
    public static string EconomyTrendLabel(EconomyTrend trend) => trend switch
    {
        EconomyTrend.Up => "Подъём",
        EconomyTrend.Stable => "Стабильность",
        EconomyTrend.Down => "Спад",
        _ => trend.ToString()
    };

    /// <summary>Русская подпись типа контракта (Блок 9.3).</summary>
    public static string ContractTypeLabel(ContractType type) => type switch
    {
        ContractType.Spot => "Разовый",
        ContractType.Recurring => "Регулярный",
        _ => type.ToString()
    };

    /// <summary>Русская подпись статуса черновика сделки — для переговорщика, который его передал (SPEC §3).</summary>
    public static string ContractDraftStatusLabel(ContractDraftStatus status) => status switch
    {
        ContractDraftStatus.AwaitingManager => "у управляющего",
        ContractDraftStatus.Submitted => "подан как заявка",
        ContractDraftStatus.Returned => "возвращён",
        ContractDraftStatus.Withdrawn => "забран",
        _ => status.ToString()
    };

    /// <summary>
    /// Условия черновика одной строкой: «40 ед. × 0.47 ¤ = 18.8 ¤ · разовая, ход 15 · штраф 10.0 %». Цена
    /// за единицу — через <see cref="FormatUnitCost"/>: в боевой модели она 0.07–0.6 ¤, и округление до
    /// целого, как у <see cref="FormatMoney"/>, показало бы 0.47 как «0 ¤».
    /// </summary>
    public static string DraftTermsLabel(ContractTerms terms)
    {
        var period = terms.Type == ContractType.Recurring ? " ед./ход" : " ед.";
        var schedule = terms.Type switch
        {
            ContractType.Spot => $"разовая, ход {terms.SpotDeliveryTurn}",
            _ when terms.RecurringEndTurn is { } endTurn => $"регулярная, {Turns(endTurn - terms.EffectiveTurn + 1)}",
            _ => "регулярная, бессрочно",
        };

        return $"{terms.Volume:0.##}{period} × {FormatUnitCost(terms.UnitPrice)} = {FormatUnitCost(terms.Volume * terms.UnitPrice)}"
            + $" · {schedule} · штраф {FormatRate(terms.PenaltyRate)}";
    }

    /// <summary>Русская подпись вида финансовой операции для истории операций «Финансов» (Блок 9.2).</summary>
    public static string FinanceOperationLabel(FinanceHistoryCalculator.OperationType type) => type switch
    {
        FinanceHistoryCalculator.OperationType.FactoryBuilt => "Постройка фабрики",
        FinanceHistoryCalculator.OperationType.FactorySold => "Продажа фабрики",
        FinanceHistoryCalculator.OperationType.WorkersHired => "Наём рабочих",
        FinanceHistoryCalculator.OperationType.WorkersFired => "Увольнение рабочих",
        FinanceHistoryCalculator.OperationType.SalariesPaid => "Зарплата рабочих",
        FinanceHistoryCalculator.OperationType.RndInvested => "Вложение в R&D",
        FinanceHistoryCalculator.OperationType.GenerationResearchInvested => "Вложение в исследование поколения",
        FinanceHistoryCalculator.OperationType.MaterialSold => "Продажа материала системе",
        FinanceHistoryCalculator.OperationType.EmergencyPurchase => "Аварийная закупка",
        FinanceHistoryCalculator.OperationType.WarehouseFee => "Плата за склад сверх лимита",
        FinanceHistoryCalculator.OperationType.FactoryUpkeep => "Содержание фабрик (капитальные затраты)",
        FinanceHistoryCalculator.OperationType.FactoryOverhead => "Затраты на работу фабрики (энергия)",
        FinanceHistoryCalculator.OperationType.FactoryOverhaul => "Капремонт фабрики",
        FinanceHistoryCalculator.OperationType.ContractDelivery => "Поставка по контракту",
        FinanceHistoryCalculator.OperationType.DeliveryMissPenalty => "Штраф за срыв поставки",
        FinanceHistoryCalculator.OperationType.ContractTerminationFee => "Плата за расторжение контракта",
        FinanceHistoryCalculator.OperationType.GrantReceived => "Грант от ведущего",
        _ => type.ToString()
    };

    /// <summary>
    /// Уточнение к строке «Поставка по контракту»/«Штраф за срыв поставки» в истории операций
    /// (Блок 9.2, запрос пользователя) — какой материал, сколько и с кем. <see langword="null"/> у
    /// операций, для которых <see cref="FinanceHistoryCalculator.FinanceOperation.MaterialName"/> не
    /// заполнен (не про контракт).
    /// </summary>
    public static string? FinanceOperationDetail(FinanceHistoryCalculator.FinanceOperation operation) =>
        operation.MaterialName is null
            ? null
            : $"{operation.MaterialName}, {operation.Volume!.Value.ToString("0.##")} ед. — {operation.CounterpartyName}";

    /// <summary>Русская подпись причины несовпадения черновиков сделки (Блок 9.3, SPEC §6).</summary>
    public static string ContractMismatchLabel(ContractMismatchReason reason) => reason switch
    {
        ContractMismatchReason.CounterpartiesDiffer => "Не совпадают покупатель/продавец",
        ContractMismatchReason.SubmittedByTheSameTeam => "Обе стороны сделки поданы одной командой",
        ContractMismatchReason.MaterialDiffers => "Не совпадает материал",
        ContractMismatchReason.TypeDiffers => "Не совпадает тип сделки (разовая/регулярная)",
        ContractMismatchReason.VolumeDiffers => "Не совпадает объём",
        ContractMismatchReason.UnitPriceDiffers => "Не совпадает цена за единицу",
        ContractMismatchReason.PenaltyRateDiffers => "Не совпадает штраф за срыв",
        ContractMismatchReason.DeliveryScheduleDiffers => "Не совпадают сроки поставки",
        _ => reason.ToString()
    };

    /// <summary>
    /// Срок действия контракта для отображения — ход поставки (spot) или диапазон/бессрочно
    /// (recurring) (Блок 9.3). Пока recurring-контракт ещё не активирован (<paramref name="status"/>
    /// — <see cref="ContractStatus.PendingConfirmation"/>), <paramref name="effectiveTurn"/>/<paramref
    /// name="recurringEndTurn"/> (если задан) — заглушка (см. <see
    /// cref="Contract.ResolveTermsForActivation"/>): показываем только согласованную длительность, не
    /// выдуманные номера ходов. У spot заглушки нет — <see cref="ContractTerms.SpotDeliveryTurn"/> с
    /// самой заявки настоящий, только может сдвинуться вперёд при активации, если исходный ход к
    /// тому моменту уже прошёл.
    /// </summary>
    public static string FormatTurnRange(ContractType type, ContractStatus status, int effectiveTurn, int? spotDeliveryTurn, int? recurringEndTurn)
    {
        if (type == ContractType.Spot)
        {
            return $"поставка на ходу {spotDeliveryTurn}";
        }

        if (status == ContractStatus.PendingConfirmation)
        {
            if (recurringEndTurn is null)
            {
                return "бессрочно, с момента подтверждения";
            }

            var duration = recurringEndTurn.Value - effectiveTurn + 1;
            return $"{duration} ходов с момента подтверждения";
        }

        return recurringEndTurn is null
            ? $"с хода {effectiveTurn}, бессрочно"
            : $"с хода {effectiveTurn} по {recurringEndTurn}";
    }

    /// <summary>
    /// Пытается посчитать себестоимость единицы материала (<see cref="MaterialCostCalculator"/> — не
    /// рыночная котировка, запрос пользователя, rebalance/2-sector-stepwise, 2026-08-21: «НЕТ НИКАКОЙ
    /// РЫНОЧНОЙ ЦЕНЫ! Есть себестоимость материала, которую мы прекрасно можем посчитать»). Возвращает
    /// <c>false</c>, если материал не производится ни одной фабрикой конфига (не должно случаться на
    /// валидном конфиге, запасной путь, чтобы дашборд не падал).
    /// </summary>
    public static bool TryCalculateUnitCost(Material product, GameSessionState state, out decimal unitCost)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(state);

        var materialCosts = MaterialCostCalculator.CalculateAll(state.Config);
        return materialCosts.TryGetValue(product.Id, out unitCost);
    }

    /// <summary>Один уровень пирамиды входов — материал, количество и глубина от корня (0 — сам продукт).</summary>
    public sealed record PyramidRow(Material Material, decimal Quantity, int Depth);

    /// <summary>
    /// Разворачивает пирамиду входов (<see cref="CostCalculator.BuildInputPyramid"/>) в плоский
    /// предпорядковый список — Razor не может естественно рекурсировать шаблон без отдельного
    /// компонента, а плоский список с глубиной проще отрисовать отступами.
    /// </summary>
    public static IReadOnlyList<PyramidRow> FlattenPyramid(InputPyramidNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var rows = new List<PyramidRow>();
        Flatten(root, 0, rows);
        return rows;
    }

    private static void Flatten(InputPyramidNode node, int depth, List<PyramidRow> rows)
    {
        rows.Add(new PyramidRow(node.Material, node.Quantity, depth));
        foreach (var input in node.Inputs)
        {
            Flatten(input, depth + 1, rows);
        }
    }

    /// <summary>
    /// Имена, которые нужны, чтобы превратить факты <see cref="TeamAttentionCalculator.AttentionItem"/>
    /// в человеческий текст: сам расчёт живёт в движке и оперирует идентификаторами, подписи — здесь
    /// (тот же приём, что у <see cref="FinanceOperationLabel"/>).
    /// </summary>
    public sealed record AttentionNaming(
        IReadOnlyDictionary<Ulid, string> FactoryNames,
        IReadOnlyDictionary<string, string> MaterialNames,
        IReadOnlyDictionary<string, string> OverhaulTierNames);

    /// <summary>
    /// Заголовок группы одинаковых поводов «Требует внимания» (блок 2 редизайна): три фабрики с одним и
    /// тем же «капремонт подорожает» раньше шли тремя одинаковыми карточками подряд (живой обход
    /// 2026-10-04). Как и <see cref="AttentionText"/>, только факт — без повелительного наклонения.
    /// </summary>
    public static string AttentionGroupHeadline(TeamAttentionCalculator.AttentionItem sample, int count) => sample switch
    {
        TeamAttentionCalculator.AttentionItem.FactoryStarvedOfInput => $"Без полной загрузки: {Factories(count)}",
        TeamAttentionCalculator.AttentionItem.FactoryWithoutWorkers => $"Без рабочих: {Factories(count)}",
        TeamAttentionCalculator.AttentionItem.FactoryInForcedDowntime => $"Вынужденный простой по износу: {Factories(count)}",
        TeamAttentionCalculator.AttentionItem.DeliveryDueAndShort => $"Поставки в ближайшем расчёте, которые нечем закрыть: {count}",
        TeamAttentionCalculator.AttentionItem.OverhaulGetsMoreExpensive => $"Капремонт подорожает: {Factories(count)}",
        TeamAttentionCalculator.AttentionItem.DeliveryAheadWillBeShort => $"Поставки впереди, которые не закрыть: {count}",
        TeamAttentionCalculator.AttentionItem.MaterialRunningOut => $"Кончаются материалы: {count}",
        _ => $"{sample.GetType().Name}: {count}",
    };

    /// <summary>
    /// Вкладка карточки фабрики, на которой лежит рычаг, связанный с поводом. Раньше переход из
    /// «Требует внимания» всегда открывал «Люди» — даже для предупреждения про капремонт (живой обход
    /// 2026-10-04). Это навигация к месту, а не совет, что там сделать.
    /// </summary>
    public static string AttentionFactoryTab(TeamAttentionCalculator.AttentionItem item) => item switch
    {
        TeamAttentionCalculator.AttentionItem.FactoryInForcedDowntime => "wear",
        TeamAttentionCalculator.AttentionItem.OverhaulGetsMoreExpensive => "wear",
        TeamAttentionCalculator.AttentionItem.FactoryStarvedOfInput => "recipe",
        TeamAttentionCalculator.AttentionItem.MaterialRunningOut => "recipe",
        _ => "workers",
    };

    /// <summary>
    /// Короткая причина повода для строки самой фабрики в цепочке «Производства» — без имени фабрики,
    /// оно уже стоит в строке. <c>null</c> — повод не про эту фабрику. Те же факты, что и в
    /// <see cref="AttentionText"/>, и то же правило: без единого «сделайте».
    /// </summary>
    public static string? AttentionFactoryReason(
        TeamAttentionCalculator.AttentionItem item, Ulid factoryId, AttentionNaming naming)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(naming);

        return item switch
        {
            TeamAttentionCalculator.AttentionItem.FactoryStarvedOfInput x when x.FactoryId == factoryId =>
                $"{Turns(x.TurnsInARow)} подряд без полной загрузки: не хватает «{Material(naming, x.MaterialId)}»",
            TeamAttentionCalculator.AttentionItem.FactoryWithoutWorkers x when x.FactoryId == factoryId =>
                "нет рабочих",
            TeamAttentionCalculator.AttentionItem.FactoryInForcedDowntime x when x.FactoryId == factoryId =>
                $"вынужденный простой по износу, осталось {Turns(x.TurnsRemaining)}",
            TeamAttentionCalculator.AttentionItem.OverhaulGetsMoreExpensive x when x.FactoryId == factoryId =>
                $"капремонт подорожает через {Turns(x.TurnsUntil)}",
            TeamAttentionCalculator.AttentionItem.MaterialRunningOut x when x.AffectedFactoryIds.Contains(factoryId) =>
                $"«{Material(naming, x.MaterialId)}» кончится через {Turns(x.TurnsUntil)}",
            _ => null,
        };
    }

    /// <summary>«1 фабрика», «3 фабрики», «5 фабрик».</summary>
    public static string Factories(int count)
    {
        var lastTwo = Math.Abs(count) % 100;
        var last = lastTwo % 10;
        var word = lastTwo is >= 11 and <= 14 ? "фабрик"
            : last == 1 ? "фабрика"
            : last is >= 2 and <= 4 ? "фабрики"
            : "фабрик";
        return $"{count} {word}";
    }

    /// <summary>
    /// Заголовок и пояснение одного повода обратить внимание. Формулировки намеренно описательные:
    /// что случилось и почему — без единого «сделайте», см. границу в doc-comment
    /// <see cref="TeamAttentionCalculator"/>.
    /// </summary>
    public static (string Headline, string Detail) AttentionText(
        TeamAttentionCalculator.AttentionItem item, AttentionNaming naming)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(naming);

        return item switch
        {
            TeamAttentionCalculator.AttentionItem.FactoryStarvedOfInput x => (
                $"{Factory(naming, x.FactoryId)}: {Turns(x.TurnsInARow)} подряд без полной загрузки",
                $"не хватает «{Material(naming, x.MaterialId)}» — {x.ShortfallPerTurn:0.##} ед. на ход"),

            TeamAttentionCalculator.AttentionItem.FactoryWithoutWorkers x => (
                $"{Factory(naming, x.FactoryId)}: нет рабочих",
                "содержание фабрики списывается каждый ход, выпуска нет"),

            TeamAttentionCalculator.AttentionItem.FactoryInForcedDowntime x => (
                $"{Factory(naming, x.FactoryId)}: вынужденный простой по износу",
                $"осталось {Turns(x.TurnsRemaining)}"),

            TeamAttentionCalculator.AttentionItem.DeliveryDueAndShort x => (
                $"Поставка «{Material(naming, x.MaterialId)}» в ближайшем расчёте: не хватает {x.Shortfall:0.##} ед.",
                $"штраф за срыв — {FormatMoney(x.Penalty)}, плюс просадка репутации"),

            TeamAttentionCalculator.AttentionItem.WarehouseOverFreeCapacity x => (
                $"Склад сверх бесплатного лимита на {x.OverageQuantity:0.##} ед.",
                $"за это списывается {FormatMoney(x.FeePerTurn)} каждый ход"),

            TeamAttentionCalculator.AttentionItem.OverhaulGetsMoreExpensive x => (
                $"{Factory(naming, x.FactoryId)}: капремонт подорожает через {Turns(x.TurnsUntil)}",
                $"сейчас сработала бы ступень «{Tier(naming, x.CurrentTierId)}», станет «{Tier(naming, x.NextTierId)}»"),

            TeamAttentionCalculator.AttentionItem.DeliveryAheadWillBeShort x => (
                $"Поставка «{Material(naming, x.MaterialId)}» через {Turns(x.TurnsUntil)}: не хватит {x.ProjectedShortfall:0.##} ед.",
                "посчитано при полной загрузке своих фабрик — то есть по самой оптимистичной оценке"),

            TeamAttentionCalculator.AttentionItem.MaterialRunningOut x => (
                $"«{Material(naming, x.MaterialId)}» кончится через {Turns(x.TurnsUntil)}",
                $"расходуется быстрее, чем производится; встанут: {string.Join(", ", x.AffectedFactoryIds.Select(id => Factory(naming, id)))}"),

            _ => (item.GetType().Name, string.Empty),
        };
    }

    /// <summary>«1 ход», «2 хода», «5 ходов» — панель читают на бегу, и падеж здесь заметен сильнее, чем кажется.</summary>
    public static string Turns(int count)
    {
        var lastTwo = Math.Abs(count) % 100;
        var last = lastTwo % 10;
        var word = lastTwo is >= 11 and <= 14 ? "ходов"
            : last == 1 ? "ход"
            : last is >= 2 and <= 4 ? "хода"
            : "ходов";

        return $"{count} {word}";
    }

    private static string Factory(AttentionNaming naming, Ulid factoryId) =>
        naming.FactoryNames.TryGetValue(factoryId, out var name) ? name : "фабрика";

    private static string Material(AttentionNaming naming, string materialId) =>
        naming.MaterialNames.TryGetValue(materialId, out var name) ? name : materialId;

    private static string Tier(AttentionNaming naming, string tierId) =>
        naming.OverhaulTierNames.TryGetValue(tierId, out var name) ? name : tierId;
}
