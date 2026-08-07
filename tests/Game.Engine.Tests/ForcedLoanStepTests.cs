namespace Game.Engine.Tests;

/// <summary>
/// Принудительный заём — отдельный шаг в самом конце расчёта тика (см. doc-comment <see
/// cref="ForcedLoanStep"/>), после финансов, производства и контрактов, а не часть <see
/// cref="TickFinanceStep"/> (баг-репорт пользователя: раньше решение принималось до переменных затрат
/// на работу фабрики и исполнения контрактов, из-за чего команда могла уйти в минус повторно уже
/// после «спасительного» займа). Сборка в полный тик — в GameSessionForcedLoanTests.
/// </summary>
public class ForcedLoanStepTests
{
    private static readonly Config.Session.StartingConditionsConfig LoanConfig = TestGameConfig.Resolved.Raw.StartingConditions;

    [Fact]
    public void Run_Returns_Null_When_The_Balance_Is_Positive()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.Credit(10m);

        Assert.Null(ForcedLoanStep.Run(team, LoanConfig));
    }

    [Fact]
    public void Run_Returns_Null_When_The_Balance_Is_Exactly_Zero()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();

        Assert.Null(ForcedLoanStep.Run(team, LoanConfig));
    }

    [Fact]
    public void Run_Covers_The_Exact_Negative_Balance_And_Escalates_The_Penalty_Rate_Surcharge()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.Debit(40m); // баланс -40

        var change = ForcedLoanStep.Run(team, LoanConfig);

        var forcedLoan = Assert.IsType<ForcedLoanTaken>(change);
        Assert.Equal(40m, forcedLoan.Amount);
        Assert.Equal(0.1m, forcedLoan.NewPenaltyRateSurcharge); // TestGameConfig: ForcedLoanPenaltyRatePerOccurrence=0.1
    }

    [Fact]
    public void Applying_Two_Consecutive_Shortfalls_Escalates_The_Penalty_Rate_Surcharge_Each_Time()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        team.Debit(40m);
        log.Append(ForcedLoanStep.Run(team, LoanConfig)!);
        Assert.Equal(0.1m, team.PenaltyRateSurcharge);
        Assert.Equal(0m, team.Balance); // заём ровно закрыл дыру

        team.Debit(15m);
        log.Append(ForcedLoanStep.Run(team, LoanConfig)!);
        Assert.Equal(0.2m, team.PenaltyRateSurcharge); // второй принудительный заём эскалирует ещё раз

        Assert.True(log.VerifyIntegrity());
    }

    [Fact]
    public void Run_Covers_Only_The_Remaining_Headroom_When_The_Shortfall_Would_Exceed_The_Debt_Cap()
    {
        var cappedConfig = LoanConfig with { MaxTotalDebt = 100m };
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.TakeLoan(70m); // Debt=70, запас до потолка 100 — 30
        team.Debit(200m); // баланс -130, дыра больше, чем оставшийся запас

        var change = ForcedLoanStep.Run(team, cappedConfig);

        var forcedLoan = Assert.IsType<ForcedLoanTaken>(change);
        Assert.Equal(30m, forcedLoan.Amount); // не вся дыра (130), а ровно оставшийся запас до потолка
    }

    [Fact]
    public void Run_Returns_Null_When_Debt_Is_Already_At_The_Cap()
    {
        var cappedConfig = LoanConfig with { MaxTotalDebt = 100m };
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.TakeLoan(100m); // Debt уже равен потолку
        team.Debit(50m); // баланс отрицательный, но занимать больше некуда

        Assert.Null(ForcedLoanStep.Run(team, cappedConfig));
    }

    [Fact]
    public void Applying_A_Capped_Forced_Loan_Leaves_The_Balance_Negative()
    {
        var cappedConfig = LoanConfig with { MaxTotalDebt = 100m };
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        team.TakeLoan(70m);
        team.Debit(200m); // баланс -130

        log.Append(ForcedLoanStep.Run(team, cappedConfig)!);

        Assert.Equal(100m, team.Debt); // упёрлись ровно в потолок, не выше
        Assert.Equal(-100m, team.Balance); // дыра покрыта только частично — баланс так и остался в минусе
    }
}
