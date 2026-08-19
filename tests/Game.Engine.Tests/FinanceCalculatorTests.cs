using Game.Config.Economy;
using Game.Config.Session;
using Game.Domain;

namespace Game.Engine.Tests;

public class FinanceCalculatorTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");

    private static readonly StartingConditionsConfig LoanConfig = new()
    {
        MaxStartingLoanAmount = 10000m,
        BaseLoanInterestRate = 0.05m,
        LoanInterestRateGrowthPerUnitBorrowed = 0.0001m,
        ForcedLoanPenaltyRatePerOccurrence = 0.1m,
        MaxReputationRatePenalty = 0.2m,
        MandatoryRepaymentRatePerTurn = 0.1m,
        MaxTotalDebt = 1_000_000_000m,
        // Заведомо выше любой ставки, встречающейся в этом файле (максимум 0.35) — не про потолок
        // ставки, для него отдельные тесты ниже (docs/TODO.md #21).
        MaxLoanInterestRate = 10m,
    };

    private static readonly WorkerProductivityConfig WorkerConfig = new()
    {
        BaseWorkerCount = 5,
        DiminishingReturnsFactor = 0.5m,
        HireCostPerWorker = 50m,
        FireCostPerWorker = 30m,
        SalaryPerWorkerPerTurn = 5m,
        // Выше числа рабочих в любом сценарии этого файла — большинство тестов не про
        // прогрессивную надбавку, у неё отдельный конфиг ниже (WorkerConfigWithSalaryEscalation).
        TeamSalaryBaseWorkerCount = 1000,
        SalaryEscalationFactor = 1.5m,
    };

    private static readonly WorkerProductivityConfig WorkerConfigWithSalaryEscalation = WorkerConfig with
    {
        TeamSalaryBaseWorkerCount = 5,
        SalaryEscalationFactor = 2m,
    };

    [Fact]
    public void CalculateInterest_Is_Zero_When_Team_Has_No_Debt()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);

        Assert.Equal(0m, FinanceCalculator.CalculateInterest(team, LoanConfig, reputationPercentage: 100m));
    }

    [Fact]
    public void CalculateInterest_Uses_The_Base_Rate_Plus_Growth_By_Debt_Size()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);
        team.TakeLoan(1000m);

        // ставка = 0.05 + 0.0001 * 1000 = 0.15 (репутация 100% -> надбавка 0); проценты = 1000 * 0.15 = 150
        Assert.Equal(0.15m, FinanceCalculator.CalculateEffectiveLoanRate(team, LoanConfig, reputationPercentage: 100m));
        Assert.Equal(150m, FinanceCalculator.CalculateInterest(team, LoanConfig, reputationPercentage: 100m));
    }

    [Fact]
    public void CalculateEffectiveLoanRate_Includes_The_Penalty_Rate_Surcharge()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);
        team.TakeLoan(1000m);
        team.IncreasePenaltyRateSurcharge(0.2m);

        Assert.Equal(0.35m, FinanceCalculator.CalculateEffectiveLoanRate(team, LoanConfig, reputationPercentage: 100m));
    }

    [Fact]
    public void CalculateEffectiveLoanRate_Adds_A_Penalty_That_Scales_Linearly_With_Lost_Reputation()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);

        // MaxReputationRatePenalty = 0.2; на 0% репутации — вся надбавка, на 50% — половина.
        Assert.Equal(0.05m, FinanceCalculator.CalculateEffectiveLoanRate(team, LoanConfig, reputationPercentage: 100m));
        Assert.Equal(0.15m, FinanceCalculator.CalculateEffectiveLoanRate(team, LoanConfig, reputationPercentage: 50m));
        Assert.Equal(0.25m, FinanceCalculator.CalculateEffectiveLoanRate(team, LoanConfig, reputationPercentage: 0m));
    }

    [Fact]
    public void CalculateEffectiveLoanRate_Raw_Values_Overload_Matches_The_Team_Based_Overload()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);
        team.TakeLoan(1000m);
        team.IncreasePenaltyRateSurcharge(0.2m);

        var fromTeam = FinanceCalculator.CalculateEffectiveLoanRate(team, LoanConfig, reputationPercentage: 50m);
        var fromRawValues = FinanceCalculator.CalculateEffectiveLoanRate(
            team.Debt, team.PenaltyRateSurcharge, reputationPercentage: 50m, LoanConfig);

        Assert.Equal(fromTeam, fromRawValues);
    }

    [Fact]
    public void CalculateEffectiveLoanRate_Preview_With_An_Additional_Amount_Is_Higher_Than_The_Current_Rate()
    {
        // предпросмотр займа (Блок 9.2, SPEC §5.9): ставка на итоговый долг после гипотетического займа.
        var currentRate = FinanceCalculator.CalculateEffectiveLoanRate(1000m, 0m, reputationPercentage: 100m, LoanConfig);
        var previewRate = FinanceCalculator.CalculateEffectiveLoanRate(1000m + 500m, 0m, reputationPercentage: 100m, LoanConfig);

        Assert.True(previewRate > currentRate);
    }

    [Fact]
    public void CalculateMandatoryRepayment_Is_Zero_When_Team_Has_No_Debt()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);

        Assert.Equal(0m, FinanceCalculator.CalculateMandatoryRepayment(team, LoanConfig));
    }

    [Fact]
    public void CalculateMandatoryRepayment_Is_A_Fixed_Fraction_Of_Current_Debt_Not_Of_The_Interest_Rate()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);
        team.TakeLoan(1000m);

        // MandatoryRepaymentRatePerTurn = 0.1, независимо от эффективной ставки процентов (0.15 здесь).
        Assert.Equal(100m, FinanceCalculator.CalculateMandatoryRepayment(team, LoanConfig));
    }

    [Fact]
    public void CalculateMandatoryRepayment_Closes_The_Whole_Debt_Once_The_Percentage_Payment_Would_Round_To_Zero()
    {
        // Процент от долга по определению никогда не доходит ровно до нуля (запрос пользователя: не
        // списывать и не логировать вечные «−0 ¤» на угасающем остатке долга) — при
        // MandatoryRepaymentRatePerTurn = 0.1 платёж уходит ниже одной денежной единицы уже при долге
        // меньше 10, и в этот момент вместо 10%-й доли гасится весь остаток разом.
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);
        team.TakeLoan(5m);

        Assert.Equal(5m, FinanceCalculator.CalculateMandatoryRepayment(team, LoanConfig));
    }

    [Fact]
    public void CalculateMandatoryRepayment_Still_Uses_The_Percentage_When_The_Payment_Is_At_Least_One_Unit()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);
        team.TakeLoan(10m);

        // 10 * 0.1 = 1 ровно — граница ещё не «меньше единицы», обычная доля остаётся в силе.
        Assert.Equal(1m, FinanceCalculator.CalculateMandatoryRepayment(team, LoanConfig));
    }

    [Fact]
    public void CalculateSalaries_Multiplies_Worker_Count_By_The_Configured_Rate()
    {
        Assert.Equal(35m, FinanceCalculator.CalculateSalaries(totalWorkers: 7, WorkerConfig));
    }

    [Fact]
    public void CalculateSalaries_Is_Zero_For_No_Workers()
    {
        Assert.Equal(0m, FinanceCalculator.CalculateSalaries(totalWorkers: 0, WorkerConfig));
    }

    [Fact]
    public void CalculateSalaries_Stays_Flat_At_Or_Below_The_Team_Base_Worker_Count()
    {
        // 5 <= TeamSalaryBaseWorkerCount(5) — как раньше, без надбавки.
        Assert.Equal(25m, FinanceCalculator.CalculateSalaries(totalWorkers: 5, WorkerConfigWithSalaryEscalation));
    }

    [Fact]
    public void CalculateSalaries_Charges_A_Higher_Rate_For_Workers_Beyond_The_Team_Base_Worker_Count()
    {
        // 5 по базовой ставке (5*5=25) + 3 сверх порога по ставке *2 (3*5*2=30) = 55.
        Assert.Equal(55m, FinanceCalculator.CalculateSalaries(totalWorkers: 8, WorkerConfigWithSalaryEscalation));
    }

    // Найдено разбором реального прогона LLM-бота (docs/TODO.md #21,
    // docs/bot-runs/2026-08-19-stage1-gpt-oss-20b/ANALYSIS.md): LoanInterestRateGrowthPerUnitBorrowed
    // растёт линейно от АБСОЛЮТНОЙ суммы долга без предела — при крупном (в т.ч. добровольном, не
    // ограниченном MaxTotalDebt) долге давало бессмысленную ставку и, вместе с ней, фактически
    // квадратичные от долга проценты за один ход.
    private static readonly StartingConditionsConfig CappedLoanConfig = LoanConfig with { MaxLoanInterestRate = 0.5m };

    [Fact]
    public void CalculateEffectiveLoanRate_ClampsToTheConfiguredCeiling_AtLargeDebt()
    {
        // ставка без потолка = 0.05 + 0.0001 * 10_000_000 = 1000.05 — далеко за пределами 0.5.
        var rate = FinanceCalculator.CalculateEffectiveLoanRate(10_000_000m, 0m, reputationPercentage: 100m, CappedLoanConfig);

        Assert.Equal(0.5m, rate);
    }

    [Fact]
    public void CalculateInterest_StaysBoundedAtLargeDebt_BecauseTheRateIsCapped()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);
        team.TakeLoan(10_000_000m);

        // Без потолка проценты были бы ~10_000_500_000 (долг × неограниченная ставка ~1000);
        // с потолком 0.5 — не больше долга, помноженного на него.
        var interest = FinanceCalculator.CalculateInterest(team, CappedLoanConfig, reputationPercentage: 100m);

        Assert.Equal(5_000_000m, interest); // 10_000_000 * 0.5
    }

    [Fact]
    public void CalculateEffectiveLoanRate_BelowTheCeiling_IsUnaffectedByTheCap()
    {
        // На небольшом долге (как в остальных тестах файла) потолок 0.5 никого не задевает.
        var rate = FinanceCalculator.CalculateEffectiveLoanRate(1000m, 0m, reputationPercentage: 100m, CappedLoanConfig);

        Assert.Equal(0.15m, rate); // тот же результат, что и без потолка (см. тест выше по файлу)
    }
}
