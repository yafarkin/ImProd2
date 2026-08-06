namespace Game.Engine.Tests;

public class TickFinanceStepTests
{
    // TestGameConfig: BaseLoanInterestRate=0.05, LoanInterestRateGrowthPerUnitBorrowed=0,
    // ForcedLoanPenaltyRatePerOccurrence=0.1, MaxReputationRatePenalty=0.1; BaseWorkerCount=5, SalaryPerWorkerPerTurn=5.
    private static readonly Config.Session.StartingConditionsConfig LoanConfig = TestGameConfig.Resolved.Raw.StartingConditions;
    private static readonly Config.Economy.WorkerProductivityConfig WorkerConfig = TestGameConfig.Resolved.Raw.WorkerProductivity;
    // TestGameConfig: FreeCapacity=1000 (намного больше остатков в этих тестах) — плата за склад не начисляется по умолчанию.
    private static readonly Config.Economy.WarehouseConfig WarehouseConfig = TestGameConfig.Resolved.Raw.Warehouse;
    // TestGameConfig.Mine/SteelMill: FixedCostPerTurn=0 — эти тесты про остальные шаги, отдельные
    // тесты на капитальные затраты — в FactoryUpkeepTests.
    private static readonly IReadOnlyList<Config.Catalog.FactoryDefinitionConfig> FactoryDefinitions = TestGameConfig.Resolved.Raw.FactoryDefinitions;
    // Ни у одной фабрики этих тестов нет RndCommitmentPerTurn (по умолчанию 0) — эти тесты не про
    // R&D, отдельные тесты на него — в TickFinanceStepRndTests.
    private static readonly Config.Economy.RndConfig RndConfig = TestGameConfig.Resolved.Raw.Rnd;
    // Ни у одной команды этих тестов нет GenerationResearchCommitmentPerTurn (по умолчанию 0) — эти
    // тесты не про исследование поколений, отдельные тесты на него — в TickFinanceStepGenerationResearchTests.
    private static readonly Config.Economy.GenerationResearchConfig GenerationResearchConfig = TestGameConfig.Resolved.Raw.GenerationResearch;

    [Fact]
    public void Run_Returns_Nothing_For_A_Debt_Free_Team_With_No_Workers()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);

        Assert.Empty(changes);
    }

    [Fact]
    public void Run_Charges_Interest_Then_Salaries_In_That_Order_When_Balance_Covers_Both()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.TakeLoan(1000m); // проценты = 1000 * 0.05 = 50
        team.Credit(1000m); // с запасом
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.Hire(4); // зарплата = 4 * 5 = 20

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);

        Assert.Equal(2, changes.Count);
        var interest = Assert.IsType<LoanInterestCharged>(changes[0]);
        Assert.Equal(50m, interest.Amount);
        var salaries = Assert.IsType<SalariesPaid>(changes[1]);
        Assert.Equal(20m, salaries.Amount);
        Assert.Equal(4, salaries.TotalWorkers);
    }

    [Fact]
    public void Run_Charges_Interest_And_Salaries_In_Full_Even_When_The_Balance_Cannot_Cover_Them()
    {
        // Раньше здесь же добавлялся принудительный заём — теперь решение о нём переехало в
        // отдельный, самый последний шаг всего тика (ForcedLoanStep, вызывается GameSession.RunTick
        // после производства и контрактов, не отсюда — баг-репорт пользователя, см. doc-comment
        // TickFinanceStep и ForcedLoanStepTests).
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.TakeLoan(1000m); // проценты = 50; TakeLoan сам зачисляет сумму на баланс...
        team.Debit(1000m); // ...поэтому сразу же её и тратим, чтобы остался только реальный остаток
        team.Credit(30m); // не хватит на проценты (50)
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.Hire(4); // зарплата = 20

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);

        Assert.Equal(2, changes.Count);
        var interest = Assert.IsType<LoanInterestCharged>(changes[0]);
        Assert.Equal(50m, interest.Amount); // не урезано из-за нехватки баланса
        var salaries = Assert.IsType<SalariesPaid>(changes[1]);
        Assert.Equal(20m, salaries.Amount);
    }

    [Fact]
    public void Run_Charges_A_Higher_Interest_Rate_When_Reputation_Is_Damaged()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.TakeLoan(1000m);
        team.Credit(1000m);

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 0m);

        // ставка = 0.05 (база) + 0.1 (вся надбавка при 0% репутации) = 0.15; проценты = 1000 * 0.15 = 150
        var interest = Assert.IsType<LoanInterestCharged>(Assert.Single(changes));
        Assert.Equal(0.15m, interest.Rate);
        Assert.Equal(150m, interest.Amount);
    }

    [Fact]
    public void Run_Charges_A_Warehouse_Fee_Last_After_Interest_And_Salaries()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.TakeLoan(1000m); // проценты = 50
        team.Credit(1000m); // с запасом
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.Hire(4); // зарплата = 20
        team.Warehouse.Add(TestGameConfig.Ore, 15m, 0m); // сверх лимита (10) на 5 единиц
        var warehouseConfig = new Config.Economy.WarehouseConfig { FreeCapacity = 10m, OverageFeePerUnit = 2m };

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, warehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);

        Assert.Equal(3, changes.Count);
        Assert.IsType<LoanInterestCharged>(changes[0]);
        Assert.IsType<SalariesPaid>(changes[1]);
        var fee = Assert.IsType<WarehouseFeeCharged>(changes[2]);
        Assert.Equal(5m, fee.OverageQuantity);
        Assert.Equal(10m, fee.Amount); // 5 * 2
    }

    [Fact]
    public void Run_Charges_An_Unpayable_Warehouse_Fee_In_Full()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.TakeLoan(1000m); // проценты = 50
        team.Debit(1000m); // сразу тратим сам заём
        team.Credit(90m); // хватит на проценты (50) и зарплату (20), но не на плату за склад
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.Hire(4); // зарплата = 20
        team.Warehouse.Add(TestGameConfig.Ore, 15m, 0m); // сверх лимита (10) на 5 единиц
        var warehouseConfig = new Config.Economy.WarehouseConfig { FreeCapacity = 10m, OverageFeePerUnit = 5m }; // плата = 25

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, warehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);

        // Было 4 (включая ForcedLoanTaken) — теперь без него, см. doc-comment класса и ForcedLoanStepTests.
        Assert.Equal(3, changes.Count);
        var fee = Assert.IsType<WarehouseFeeCharged>(changes[2]);
        Assert.Equal(25m, fee.Amount); // не урезано из-за нехватки баланса; баланс после: 90-50-20-25 = -5
    }

    [Fact]
    public void Run_Charges_No_Fee_When_Stock_Is_Within_Free_Capacity()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.Warehouse.Add(TestGameConfig.Ore, 5m, 0m); // намного меньше лимита по умолчанию (1000)

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);

        Assert.Empty(changes);
    }

    // TestGameConfig.LoanConfig держит MandatoryRepaymentRatePerTurn=0 (чтобы не менять ожидания
    // всех остальных тестов этого файла, которые его не касаются) — для этих тестов берём свою
    // копию с ненулевой долей через `with`, не трогая остальные поля.
    private static readonly Config.Session.StartingConditionsConfig RepaymentLoanConfig =
        LoanConfig with { MandatoryRepaymentRatePerTurn = 0.1m };

    [Fact]
    public void Run_Charges_Interest_Then_Mandatory_Repayment_Then_Salaries_In_That_Order()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.TakeLoan(1000m); // проценты = 1000 * 0.05 = 50; обязательный платёж = 1000 * 0.1 = 100
        team.Credit(1000m); // с запасом
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.Hire(4); // зарплата = 20

        var changes = TickFinanceStep.Run(team, RepaymentLoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);

        Assert.Equal(3, changes.Count);
        Assert.IsType<LoanInterestCharged>(changes[0]);
        var repayment = Assert.IsType<MandatoryLoanRepaymentCharged>(changes[1]);
        Assert.Equal(100m, repayment.Amount);
        Assert.Equal(0.1m, repayment.Rate);
        Assert.IsType<SalariesPaid>(changes[2]);
    }

    [Fact]
    public void Applying_The_Mandatory_Repayment_Actually_Reduces_The_Debt()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        team.TakeLoan(1000m);
        team.Credit(1000m);

        foreach (var change in TickFinanceStep.Run(team, RepaymentLoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m))
        {
            log.Append(change);
        }

        Assert.Equal(900m, team.Debt); // 1000 - 1000*0.1
    }

    [Fact]
    public void An_Unaffordable_Mandatory_Repayment_Is_Still_Charged_In_Full()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        team.TakeLoan(1000m); // обязательный платёж = 100, процентов при этой ставке ниже нет (сумма займа сразу же обнулена)
        team.Debit(1000m); // баланс обнулён — платить обязательный платёж (100) нечем

        var changes = TickFinanceStep.Run(team, RepaymentLoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);
        foreach (var change in changes)
        {
            log.Append(change);
        }

        var repayment = Assert.IsType<MandatoryLoanRepaymentCharged>(changes[1]);
        Assert.Equal(100m, repayment.Amount); // платёж не урезается из-за нехватки баланса
        // Принудительный заём, который раньше покрывал эту дыру здесь же, теперь отдельный, самый
        // последний шаг всего тика (ForcedLoanStep, см. doc-comment класса и ForcedLoanStepTests) —
        // эта функция сама по себе просто оставляет баланс в минусе.
        Assert.Equal(-150m, team.Balance); // проценты (50) + обязательный платёж (100)
        Assert.Equal(900m, team.Debt); // 1000 - 100 (обязательный платёж), проценты тело долга не трогают
    }
}
