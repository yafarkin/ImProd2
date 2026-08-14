using Game.Config.Loading;

namespace Game.Engine;

/// <summary>
/// История всех денежных операций одной команды для дашборда (вкладка «Финансы», Блок 9.2) —
/// движок сам ничего не хранит отдельно (как и <see cref="FactoryHistoryCalculator"/>/
/// <see cref="TurnHistoryCalculator"/> для своих экранов), поэтому список восстанавливается
/// проигрыванием журнала. Перечисляет каждый <c>Change</c>, который трогает <see
/// cref="Game.Domain.Team.Balance"/> или <see cref="Game.Domain.Team.Debt"/> команды — не только
/// кредитные, но и постройку фабрик, наём/увольнение, зарплаты, R&amp;D фабрик и командное
/// исследование поколения, продажи/закупки и исполнение контрактов. Выдумывать операции, которых в
/// движке нет (например, «закрытие вклада»), нельзя — вклады пока не реализованы.
/// </summary>
public static class FinanceHistoryCalculator
{
    /// <summary>Виды денежных операций, которые реально существуют в движке.</summary>
    public enum OperationType
    {
        /// <summary>Команда сама взяла заём (<see cref="LoanTaken"/>).</summary>
        LoanTaken,

        /// <summary>Системе пришлось взять заём за команду — баланса не хватило на расходы хода (<see cref="ForcedLoanTaken"/>).</summary>
        ForcedLoan,

        /// <summary>Списаны проценты по долгу за ход; тело долга не меняют (<see cref="LoanInterestCharged"/>).</summary>
        InterestCharged,

        /// <summary>Обязательный платёж по телу долга за ход (<see cref="MandatoryLoanRepaymentCharged"/>).</summary>
        MandatoryRepayment,

        /// <summary>Команда сама досрочно погасила часть долга сверх обязательного платежа (<see cref="LoanRepaid"/>).</summary>
        VoluntaryRepayment,

        /// <summary>Постройка фабрики (<see cref="FactoryBuilt"/>).</summary>
        FactoryBuilt,

        /// <summary>Продажа (ликвидация) фабрики (<see cref="FactorySold"/>).</summary>
        FactorySold,

        /// <summary>Разовая плата за наём рабочих (<see cref="WorkersHired"/>).</summary>
        WorkersHired,

        /// <summary>Разовая плата за увольнение рабочих (<see cref="WorkersFired"/>).</summary>
        WorkersFired,

        /// <summary>Зарплата всем рабочим команды за ход (<see cref="SalariesPaid"/>).</summary>
        SalariesPaid,

        /// <summary>Вложение в R&amp;D фабрики (<see cref="RndInvested"/>).</summary>
        RndInvested,

        /// <summary>Вложение в командное исследование следующего поколения (<see cref="GenerationResearchInvested"/>).</summary>
        GenerationResearchInvested,

        /// <summary>Продажа материала системе (<see cref="MaterialSoldToSystem"/>).</summary>
        MaterialSold,

        /// <summary>Аварийная закупка материала у системы (<see cref="EmergencyPurchased"/>).</summary>
        EmergencyPurchase,

        /// <summary>Плата за превышение бесплатного лимита склада за ход (<see cref="WarehouseFeeCharged"/>).</summary>
        WarehouseFee,

        /// <summary>Капитальные затраты на содержание построенных фабрик за ход (<see cref="FactoryUpkeepPaid"/>).</summary>
        FactoryUpkeep,

        /// <summary>Переменные затраты на работу фабрики за ход — энергия, растёт с объёмом выпуска (<see cref="FactoryProduced.OverheadCost"/>).</summary>
        FactoryOverhead,

        /// <summary>Исполнение поставки по контракту — оплата (мы покупатель) или поступление (мы продавец) (<see cref="ContractDelivered"/>).</summary>
        ContractDelivery,

        /// <summary>Штраф за срыв поставки по контракту — списание с продавца или компенсация покупателю (<see cref="DeliveryMissed"/>).</summary>
        DeliveryMissPenalty,

        /// <summary>Плата за одностороннее расторжение контракта (<see cref="ContractTerminated"/>).</summary>
        ContractTerminationFee,

        /// <summary>Безвозмездный грант от ведущего (<see cref="GrantIssued"/>).</summary>
        GrantReceived,
    }

    /// <summary>Приход или расход — для отображения знака и цвета суммы на экране.</summary>
    public enum MoneyDirection
    {
        Income,
        Expense,
    }

    /// <summary>
    /// Одна строка истории — точное время записи в журнал, ход, тип операции, направление, сумма и
    /// ставка, если у операции она есть (иначе <see langword="null"/>). <see cref="FactoryId"/> —
    /// какая именно фабрика вызвала расход/доход, если операция привязана к одной конкретной фабрике
    /// (постройка, наём/увольнение, R&amp;D, переменные затраты на выпуск), иначе <see
    /// langword="null"/> (например, содержание фабрик списывается сразу по всем сразу).
    /// <see cref="MaterialName"/>/<see cref="Volume"/>/<see cref="CounterpartyName"/> заполнены
    /// только у <see cref="OperationType.ContractDelivery"/> и <see
    /// cref="OperationType.DeliveryMissPenalty"/> (запрос пользователя: живой лог с успешной
    /// поставкой нефти давал только «Поставка по контракту, сумма», без ответа на «поставлено чего,
    /// сколько и кем/кому») — у остальных видов операций своя достаточная деталь (например, фабрика).
    /// </summary>
    public sealed record FinanceOperation(
        DateTimeOffset Timestamp, int Turn, OperationType Type, MoneyDirection Direction, decimal Amount, decimal? Rate,
        Ulid? FactoryId = null, string? MaterialName = null, decimal? Volume = null, string? CounterpartyName = null);

    /// <summary>Можно звать в любой момент сессии; для команды без единой денежной операции список выходит пустым.</summary>
    public static IReadOnlyList<FinanceOperation> Summarize(
        IReadOnlyList<EventLogEntry<GameSessionState>> entries, ResolvedGameConfig config, Ulid teamId)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(config);

        var scratch = new GameSessionState(config);
        var operations = new List<FinanceOperation>();

        foreach (var entry in entries)
        {
            // Долг команды до применения этого события — нужен только для GrantIssued с
            // RepayDebtFirst (см. case ниже), чтобы показать реально погашенную сумму, а не
            // пересчитывать её задним числом из уже применённого состояния.
            var debtBeforeGrant = entry.Change is GrantIssued grant && grant.TeamId == teamId && scratch.Teams.ContainsKey(teamId)
                ? scratch.Teams[teamId].Debt
                : (decimal?)null;

            entry.Change.Apply(scratch);

            switch (entry.Change)
            {
                case LoanTaken change when change.TeamId == teamId:
                    operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.LoanTaken, MoneyDirection.Income, change.Amount, Rate: null));
                    break;
                case ForcedLoanTaken change when change.TeamId == teamId:
                    operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.ForcedLoan, MoneyDirection.Income, change.Amount, Rate: null));
                    break;
                case LoanInterestCharged change when change.TeamId == teamId:
                    operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.InterestCharged, MoneyDirection.Expense, change.Amount, change.Rate));
                    break;
                case MandatoryLoanRepaymentCharged change when change.TeamId == teamId:
                    operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.MandatoryRepayment, MoneyDirection.Expense, change.Amount, change.Rate));
                    break;
                case LoanRepaid change when change.TeamId == teamId:
                    operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.VoluntaryRepayment, MoneyDirection.Expense, change.Amount, Rate: null));
                    break;
                case FactoryBuilt change when change.TeamId == teamId && change.Cost > 0:
                    operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.FactoryBuilt, MoneyDirection.Expense, change.Cost, Rate: null, change.FactoryId));
                    break;
                case FactorySold change when change.TeamId == teamId && change.Amount > 0:
                    operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.FactorySold, MoneyDirection.Income, change.Amount, Rate: null, change.FactoryId));
                    break;
                case WorkersHired change when change.TeamId == teamId && change.Cost > 0:
                    operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.WorkersHired, MoneyDirection.Expense, change.Cost, Rate: null, change.FactoryId));
                    break;
                case WorkersFired change when change.TeamId == teamId && change.Cost > 0:
                    operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.WorkersFired, MoneyDirection.Expense, change.Cost, Rate: null, change.FactoryId));
                    break;
                case SalariesPaid change when change.TeamId == teamId && change.Amount > 0:
                    operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.SalariesPaid, MoneyDirection.Expense, change.Amount, Rate: null));
                    break;
                case RndInvested change when change.TeamId == teamId:
                    operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.RndInvested, MoneyDirection.Expense, change.Amount, Rate: null, change.FactoryId));
                    break;
                case GenerationResearchInvested change when change.TeamId == teamId:
                    operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.GenerationResearchInvested, MoneyDirection.Expense, change.Amount, Rate: null));
                    break;
                case MaterialSoldToSystem change when change.TeamId == teamId && change.TotalRevenue > 0:
                    operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.MaterialSold, MoneyDirection.Income, change.TotalRevenue, Rate: null));
                    break;
                case EmergencyPurchased change when change.TeamId == teamId && change.TotalCost > 0:
                    operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.EmergencyPurchase, MoneyDirection.Expense, change.TotalCost, Rate: null));
                    break;
                case WarehouseFeeCharged change when change.TeamId == teamId && change.Amount > 0:
                    operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.WarehouseFee, MoneyDirection.Expense, change.Amount, Rate: null));
                    break;
                case FactoryUpkeepPaid change when change.TeamId == teamId && change.Amount > 0:
                    operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.FactoryUpkeep, MoneyDirection.Expense, change.Amount, Rate: null));
                    break;
                case FactoryProduced change when change.TeamId == teamId && change.OverheadCost > 0:
                    operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.FactoryOverhead, MoneyDirection.Expense, change.OverheadCost, Rate: null, change.FactoryId));
                    break;
                case GrantIssued change when change.TeamId == teamId:
                    operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.GrantReceived, MoneyDirection.Income, change.Amount, Rate: null));
                    if (change.RepayDebtFirst && debtBeforeGrant is > 0)
                    {
                        var repayment = Math.Min(change.Amount, debtBeforeGrant.Value);
                        operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.VoluntaryRepayment, MoneyDirection.Expense, repayment, Rate: null));
                    }
                    break;
                case ContractDelivered change:
                {
                    var contract = scratch.Contracts[change.ContractId];
                    var sum = contract.Terms.Volume * contract.Terms.UnitPrice;
                    var materialName = contract.Terms.Material.Name;
                    var volume = contract.Terms.Volume;
                    if (sum > 0 && contract.BuyerTeamId == teamId)
                    {
                        var counterpartyName = scratch.Teams[contract.SellerTeamId].Name;
                        operations.Add(new FinanceOperation(
                            entry.Timestamp, scratch.CurrentTurn, OperationType.ContractDelivery, MoneyDirection.Expense, sum, Rate: null,
                            MaterialName: materialName, Volume: volume, CounterpartyName: counterpartyName));
                    }
                    else if (sum > 0 && contract.SellerTeamId == teamId)
                    {
                        var counterpartyName = scratch.Teams[contract.BuyerTeamId].Name;
                        operations.Add(new FinanceOperation(
                            entry.Timestamp, scratch.CurrentTurn, OperationType.ContractDelivery, MoneyDirection.Income, sum, Rate: null,
                            MaterialName: materialName, Volume: volume, CounterpartyName: counterpartyName));
                    }
                    break;
                }
                case DeliveryMissed change when change.PenaltyAmount > 0:
                {
                    var contract = scratch.Contracts[change.ContractId];
                    var materialName = contract.Terms.Material.Name;
                    if (contract.SellerTeamId == teamId)
                    {
                        var counterpartyName = scratch.Teams[contract.BuyerTeamId].Name;
                        operations.Add(new FinanceOperation(
                            entry.Timestamp, scratch.CurrentTurn, OperationType.DeliveryMissPenalty, MoneyDirection.Expense, change.PenaltyAmount, Rate: null,
                            MaterialName: materialName, Volume: change.ShortfallVolume, CounterpartyName: counterpartyName));
                    }
                    else if (contract.BuyerTeamId == teamId)
                    {
                        var counterpartyName = scratch.Teams[contract.SellerTeamId].Name;
                        operations.Add(new FinanceOperation(
                            entry.Timestamp, scratch.CurrentTurn, OperationType.DeliveryMissPenalty, MoneyDirection.Income, change.PenaltyAmount, Rate: null,
                            MaterialName: materialName, Volume: change.ShortfallVolume, CounterpartyName: counterpartyName));
                    }
                    break;
                }
                case ContractTerminated change when change.Fee > 0 && change.TerminatingTeamId == teamId:
                    operations.Add(new FinanceOperation(entry.Timestamp, scratch.CurrentTurn, OperationType.ContractTerminationFee, MoneyDirection.Expense, change.Fee, Rate: null));
                    break;
            }
        }

        return operations;
    }
}
