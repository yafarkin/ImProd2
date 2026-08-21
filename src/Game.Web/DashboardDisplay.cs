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
        ContractMismatchReason.TermsDiffer => "Не совпадают условия сделки",
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
    /// Пытается посчитать себестоимость единицы материала (<see cref="CostCalculator.CalculateUnitCost"/>),
    /// используя текущие рыночные котировки сырья как базовую цену. Возвращает <c>false</c>, если
    /// котировки для какого-то из видов сырья в цепочке ещё нет (например, самый первый ход до
    /// первого <see cref="MarketUpdated"/>) — дашборд в этом случае просто не показывает число, а не падает.
    /// </summary>
    public static bool TryCalculateUnitCost(Material product, GameSessionState state, out decimal unitCost)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(state);

        var rawMaterialCosts = state.Config.Materials.Values
            .Where(m => m.IsRawMaterial && state.Market.HasQuote(m.Id))
            .ToDictionary(m => m, m => state.Market.QuoteOf(m.Id).Price);

        try
        {
            unitCost = CostCalculator.CalculateUnitCost(product, state.Config.RecipeBook, rawMaterialCosts);
            return true;
        }
        catch (ArgumentException)
        {
            unitCost = 0m;
            return false;
        }
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
}
