using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>История финансовых операций одной команды для вкладки «Финансы» (Блок 9.2) — реплей журнала, тот же приём проверки, что и у <see cref="FactoryHistoryCalculatorTests"/>.</summary>
public class FinanceHistoryCalculatorTests
{
    [Fact]
    public void Summarize_Returns_Empty_For_A_Team_With_No_Financial_Events()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();

        var operations = FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, team.Id);

        Assert.Empty(operations);
    }

    [Fact]
    public void Summarize_Captures_LoanTaken()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        var entry = log.Append(new LoanTaken { Id = Ulid.NewUlid(), TeamId = team.Id, Amount = 500m });

        var operation = Assert.Single(FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, team.Id));

        Assert.Equal(FinanceHistoryCalculator.OperationType.LoanTaken, operation.Type);
        Assert.Equal(500m, operation.Amount);
        Assert.Null(operation.Rate);
        // Точное время записи в журнал (запрос пользователя: видеть, когда реально было совершено
        // действие, а не только на каком ходу) — то же значение, что несёт сама запись журнала.
        Assert.Equal(entry.Timestamp, operation.Timestamp);
    }

    [Fact]
    public void Summarize_Captures_ForcedLoanTaken()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        log.Append(new ForcedLoanTaken { Id = Ulid.NewUlid(), TeamId = team.Id, Amount = 40m, NewPenaltyRateSurcharge = 0.1m });

        var operation = Assert.Single(FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, team.Id));

        Assert.Equal(FinanceHistoryCalculator.OperationType.ForcedLoan, operation.Type);
        Assert.Equal(40m, operation.Amount);
    }

    [Fact]
    public void Summarize_Captures_LoanInterestCharged_With_Its_Rate()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        log.Append(new LoanInterestCharged { Id = Ulid.NewUlid(), TeamId = team.Id, Amount = 50m, Rate = 0.05m });

        var operation = Assert.Single(FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, team.Id));

        Assert.Equal(FinanceHistoryCalculator.OperationType.InterestCharged, operation.Type);
        Assert.Equal(50m, operation.Amount);
        Assert.Equal(0.05m, operation.Rate);
    }

    [Fact]
    public void Summarize_Captures_MandatoryLoanRepaymentCharged_With_Its_Rate()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        log.Append(new LoanTaken { Id = Ulid.NewUlid(), TeamId = team.Id, Amount = 1000m });
        log.Append(new MandatoryLoanRepaymentCharged { Id = Ulid.NewUlid(), TeamId = team.Id, Amount = 100m, Rate = 0.1m });

        var operation = Assert.Single(
            FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, team.Id),
            o => o.Type == FinanceHistoryCalculator.OperationType.MandatoryRepayment);

        Assert.Equal(FinanceHistoryCalculator.OperationType.MandatoryRepayment, operation.Type);
        Assert.Equal(100m, operation.Amount);
        Assert.Equal(0.1m, operation.Rate);
    }

    [Fact]
    public void Summarize_Captures_LoanRepaid()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        log.Append(new LoanTaken { Id = Ulid.NewUlid(), TeamId = team.Id, Amount = 500m });
        log.Append(new LoanRepaid { Id = Ulid.NewUlid(), TeamId = team.Id, Amount = 200m });

        var operations = FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, team.Id);

        Assert.Equal(2, operations.Count);
        Assert.Equal(FinanceHistoryCalculator.OperationType.VoluntaryRepayment, operations[1].Type);
        Assert.Equal(200m, operations[1].Amount);
        Assert.Null(operations[1].Rate);
    }

    [Fact]
    public void Summarize_Preserves_Chronological_Order()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        log.Append(new LoanTaken { Id = Ulid.NewUlid(), TeamId = team.Id, Amount = 500m });
        log.Append(new LoanInterestCharged { Id = Ulid.NewUlid(), TeamId = team.Id, Amount = 25m, Rate = 0.05m });
        log.Append(new LoanRepaid { Id = Ulid.NewUlid(), TeamId = team.Id, Amount = 100m });

        var operations = FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, team.Id);

        Assert.Equal(
            new[]
            {
                FinanceHistoryCalculator.OperationType.LoanTaken,
                FinanceHistoryCalculator.OperationType.InterestCharged,
                FinanceHistoryCalculator.OperationType.VoluntaryRepayment,
            },
            operations.Select(o => o.Type));
    }

    [Fact]
    public void Summarize_Only_Reports_The_Requested_Teams_Operations()
    {
        var (log, buyer, seller) = TestGameConfig.StartSessionWithTwoTeams();
        log.Append(new LoanTaken { Id = Ulid.NewUlid(), TeamId = buyer.Id, Amount = 500m });
        log.Append(new LoanTaken { Id = Ulid.NewUlid(), TeamId = seller.Id, Amount = 700m });

        var buyerOperations = FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, buyer.Id);

        var operation = Assert.Single(buyerOperations);
        Assert.Equal(500m, operation.Amount);
    }

    [Fact]
    public void Summarize_Tags_Each_Operation_With_The_Turn_It_Happened_On()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam(startingLoan: 0m);
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision, ход 1
        session.TakeLoan(teamId, 500m);

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        session.RunTick(new Random(1));
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision, ход 2
        session.RepayLoan(teamId, 50m);

        var operations = FinanceHistoryCalculator.Summarize(session.Entries, TestGameConfig.Resolved, teamId);

        var loanTaken = Assert.Single(operations, o => o.Type == FinanceHistoryCalculator.OperationType.LoanTaken);
        Assert.Equal(1, loanTaken.Turn);
        var repaid = Assert.Single(operations, o => o.Type == FinanceHistoryCalculator.OperationType.VoluntaryRepayment);
        Assert.Equal(2, repaid.Turn);
    }

    [Fact]
    public void Summarize_Marks_A_Loan_As_Income_And_Interest_As_Expense()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        log.Append(new LoanTaken { Id = Ulid.NewUlid(), TeamId = team.Id, Amount = 500m });
        log.Append(new LoanInterestCharged { Id = Ulid.NewUlid(), TeamId = team.Id, Amount = 25m, Rate = 0.05m });

        var operations = FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, team.Id);

        Assert.Equal(FinanceHistoryCalculator.MoneyDirection.Income, operations[0].Direction);
        Assert.Equal(FinanceHistoryCalculator.MoneyDirection.Expense, operations[1].Direction);
    }

    [Fact]
    public void Summarize_Captures_FactoryBuilt_As_An_Expense()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        var factoryId = Ulid.NewUlid();
        log.Append(new FactoryBuilt
        {
            Id = Ulid.NewUlid(), TeamId = team.Id, FactoryId = factoryId,
            FactoryDefinitionId = TestGameConfig.Mine.Id, RecipeId = TestGameConfig.Mine.Recipes[0].Id, Cost = 100m,
        });

        var operation = Assert.Single(FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, team.Id));

        Assert.Equal(FinanceHistoryCalculator.OperationType.FactoryBuilt, operation.Type);
        Assert.Equal(FinanceHistoryCalculator.MoneyDirection.Expense, operation.Direction);
        Assert.Equal(100m, operation.Amount);
    }

    [Fact]
    public void Summarize_Captures_WorkersHired_And_WorkersFired_As_Expenses()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        var factoryId = Ulid.NewUlid();
        log.Append(new FactoryBuilt
        {
            Id = Ulid.NewUlid(), TeamId = team.Id, FactoryId = factoryId,
            FactoryDefinitionId = TestGameConfig.Mine.Id, RecipeId = TestGameConfig.Mine.Recipes[0].Id, Cost = 100m,
        });
        log.Append(new WorkersHired { Id = Ulid.NewUlid(), TeamId = team.Id, FactoryId = factoryId, Count = 3, Cost = 150m });
        log.Append(new WorkersFired { Id = Ulid.NewUlid(), TeamId = team.Id, FactoryId = factoryId, Count = 1, Cost = 60m });

        var operations = FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, team.Id);

        var hired = Assert.Single(operations, o => o.Type == FinanceHistoryCalculator.OperationType.WorkersHired);
        Assert.Equal(FinanceHistoryCalculator.MoneyDirection.Expense, hired.Direction);
        Assert.Equal(150m, hired.Amount);
        var fired = Assert.Single(operations, o => o.Type == FinanceHistoryCalculator.OperationType.WorkersFired);
        Assert.Equal(FinanceHistoryCalculator.MoneyDirection.Expense, fired.Direction);
        Assert.Equal(60m, fired.Amount);
    }

    [Fact]
    public void Summarize_Captures_SalariesPaid_And_RndInvested_As_Expenses()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        var factoryId = Ulid.NewUlid();
        log.Append(new FactoryBuilt
        {
            Id = Ulid.NewUlid(), TeamId = team.Id, FactoryId = factoryId,
            FactoryDefinitionId = TestGameConfig.Mine.Id, RecipeId = TestGameConfig.Mine.Recipes[0].Id, Cost = 100m,
        });
        log.Append(new SalariesPaid { Id = Ulid.NewUlid(), TeamId = team.Id, TotalWorkers = 3, Amount = 90m });
        log.Append(new RndInvested { Id = Ulid.NewUlid(), TeamId = team.Id, FactoryId = factoryId, Amount = 50m });

        var operations = FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, team.Id);

        var salaries = Assert.Single(operations, o => o.Type == FinanceHistoryCalculator.OperationType.SalariesPaid);
        Assert.Equal(FinanceHistoryCalculator.MoneyDirection.Expense, salaries.Direction);
        Assert.Equal(90m, salaries.Amount);
        var rnd = Assert.Single(operations, o => o.Type == FinanceHistoryCalculator.OperationType.RndInvested);
        Assert.Equal(FinanceHistoryCalculator.MoneyDirection.Expense, rnd.Direction);
        Assert.Equal(50m, rnd.Amount);
    }

    [Fact]
    public void Summarize_Captures_MaterialSold_As_Income_And_EmergencyPurchase_As_Expense()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        log.Append(new EmergencyPurchased { Id = Ulid.NewUlid(), TeamId = team.Id, MaterialId = "ore", Volume = 20m, UnitPrice = 10m, TotalCost = 200m });
        log.Append(new MaterialSoldToSystem
        {
            Id = Ulid.NewUlid(), TeamId = team.Id, MaterialId = "ore", Volume = 20m,
            WithinCapacityVolume = 20m, OverflowVolume = 0m, UnitPrice = 10m, TotalRevenue = 200m,
        });

        var operations = FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, team.Id);

        var purchase = Assert.Single(operations, o => o.Type == FinanceHistoryCalculator.OperationType.EmergencyPurchase);
        Assert.Equal(FinanceHistoryCalculator.MoneyDirection.Expense, purchase.Direction);
        Assert.Equal(200m, purchase.Amount);
        var sale = Assert.Single(operations, o => o.Type == FinanceHistoryCalculator.OperationType.MaterialSold);
        Assert.Equal(FinanceHistoryCalculator.MoneyDirection.Income, sale.Direction);
        Assert.Equal(200m, sale.Amount);
    }

    [Fact]
    public void Summarize_Captures_WarehouseFeeCharged_As_An_Expense()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        log.Append(new WarehouseFeeCharged { Id = Ulid.NewUlid(), TeamId = team.Id, OverageQuantity = 50m, Amount = 15m });

        var operation = Assert.Single(FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, team.Id));

        Assert.Equal(FinanceHistoryCalculator.OperationType.WarehouseFee, operation.Type);
        Assert.Equal(FinanceHistoryCalculator.MoneyDirection.Expense, operation.Direction);
        Assert.Equal(15m, operation.Amount);
    }

    [Fact]
    public void Summarize_Captures_GrantReceived_As_Income()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        log.Append(new GrantIssued { Id = Ulid.NewUlid(), TeamId = team.Id, Amount = 300m });

        var operation = Assert.Single(FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, team.Id));

        Assert.Equal(FinanceHistoryCalculator.OperationType.GrantReceived, operation.Type);
        Assert.Equal(FinanceHistoryCalculator.MoneyDirection.Income, operation.Direction);
        Assert.Equal(300m, operation.Amount);
    }

    private static ContractSpec SheetSpot(Ulid buyerId, Ulid sellerId, decimal volume = 10m, decimal unitPrice = 20m, decimal penaltyRate = 0.1m)
    {
        var terms = new ContractTerms(
            ContractType.Spot, TestGameConfig.Sheet, volume, unitPrice, penaltyRate,
            effectiveTurn: 1, spotDeliveryTurn: 1, recurringEndTurn: null);
        var contract = new Contract(Ulid.NewUlid(), buyerId, sellerId, terms, "ABC123");

        return ContractSpec.From(contract);
    }

    [Fact]
    public void Summarize_Captures_ContractDelivery_As_An_Expense_For_The_Buyer_And_Income_For_The_Seller()
    {
        var (log, buyer, seller) = TestGameConfig.StartSessionWithTwoTeams();
        // Склад продавца пополняется через настоящее журналируемое событие (а не прямой вызов
        // Warehouse.Add на живом Team), иначе собственный реплей FinanceHistoryCalculator.Summarize
        // на свежем scratch-состоянии не увидит этот товар и упадёт на ContractDelivered.
        log.Append(new EmergencyPurchased { Id = Ulid.NewUlid(), TeamId = seller.Id, MaterialId = TestGameConfig.Sheet.Id, Volume = 10m, UnitPrice = 0m, TotalCost = 0m });
        var spec = SheetSpot(buyer.Id, seller.Id, volume: 10m, unitPrice: 20m);
        log.Append(new ContractSigned { Id = Ulid.NewUlid(), Contract = spec });
        log.Append(new ContractConfirmed { Id = Ulid.NewUlid(), ContractId = spec.ContractId });
        log.Append(new ContractDelivered { Id = Ulid.NewUlid(), ContractId = spec.ContractId, Turn = 1 });

        var buyerOperation = Assert.Single(FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, buyer.Id));
        Assert.Equal(FinanceHistoryCalculator.OperationType.ContractDelivery, buyerOperation.Type);
        Assert.Equal(FinanceHistoryCalculator.MoneyDirection.Expense, buyerOperation.Direction);
        Assert.Equal(200m, buyerOperation.Amount);

        var sellerOperation = Assert.Single(FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, seller.Id));
        Assert.Equal(FinanceHistoryCalculator.OperationType.ContractDelivery, sellerOperation.Type);
        Assert.Equal(FinanceHistoryCalculator.MoneyDirection.Income, sellerOperation.Direction);
        Assert.Equal(200m, sellerOperation.Amount);
    }

    [Fact]
    public void Summarize_Captures_DeliveryMissPenalty_As_An_Expense_For_The_Seller_And_Income_For_The_Buyer()
    {
        var (log, buyer, seller) = TestGameConfig.StartSessionWithTwoTeams();
        var spec = SheetSpot(buyer.Id, seller.Id, volume: 10m, unitPrice: 20m, penaltyRate: 0.1m);
        log.Append(new ContractSigned { Id = Ulid.NewUlid(), Contract = spec });
        log.Append(new ContractConfirmed { Id = Ulid.NewUlid(), ContractId = spec.ContractId });
        log.Append(new DeliveryMissed { Id = Ulid.NewUlid(), ContractId = spec.ContractId, Turn = 1, ShortfallVolume = 10m, PenaltyAmount = 20m });

        var sellerOperation = Assert.Single(FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, seller.Id));
        Assert.Equal(FinanceHistoryCalculator.OperationType.DeliveryMissPenalty, sellerOperation.Type);
        Assert.Equal(FinanceHistoryCalculator.MoneyDirection.Expense, sellerOperation.Direction);
        Assert.Equal(20m, sellerOperation.Amount);

        var buyerOperation = Assert.Single(FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, buyer.Id));
        Assert.Equal(FinanceHistoryCalculator.OperationType.DeliveryMissPenalty, buyerOperation.Type);
        Assert.Equal(FinanceHistoryCalculator.MoneyDirection.Income, buyerOperation.Direction);
        Assert.Equal(20m, buyerOperation.Amount);
    }

    [Fact]
    public void Summarize_Captures_ContractTerminationFee_As_An_Expense_For_The_Terminating_Team()
    {
        var (log, buyer, seller) = TestGameConfig.StartSessionWithTwoTeams();
        var spec = SheetSpot(buyer.Id, seller.Id, volume: 10m, unitPrice: 20m);
        log.Append(new ContractSigned { Id = Ulid.NewUlid(), Contract = spec });
        log.Append(new ContractConfirmed { Id = Ulid.NewUlid(), ContractId = spec.ContractId });
        log.Append(new ContractTerminated
        {
            Id = Ulid.NewUlid(), ContractId = spec.ContractId, Turn = 1,
            Reason = ContractTerminationReason.Voluntary, TerminatingTeamId = buyer.Id, Fee = 50m,
        });

        var buyerOperation = Assert.Single(FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, buyer.Id));
        Assert.Equal(FinanceHistoryCalculator.OperationType.ContractTerminationFee, buyerOperation.Type);
        Assert.Equal(FinanceHistoryCalculator.MoneyDirection.Expense, buyerOperation.Direction);
        Assert.Equal(50m, buyerOperation.Amount);

        Assert.Empty(FinanceHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, seller.Id));
    }
}
