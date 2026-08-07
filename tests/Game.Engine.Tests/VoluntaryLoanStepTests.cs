namespace Game.Engine.Tests;

/// <summary>
/// Разрешение добровольных решений по кредиту на расчёте (см. doc-comment <see
/// cref="VoluntaryLoanStep"/>) — юниты самого шага; сборка в полный тик через <see
/// cref="GameSession"/> — в <see cref="GameSessionLoanTests"/>.
/// </summary>
public class VoluntaryLoanStepTests
{
    [Fact]
    public void Run_Returns_Nothing_When_There_Are_No_Pending_Requests()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();

        Assert.Empty(VoluntaryLoanStep.Run(team));
    }

    [Fact]
    public void Run_Emits_LoanTaken_For_A_Pending_Take_Request()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.RequestLoan(500m);

        var changes = VoluntaryLoanStep.Run(team);

        var loanTaken = Assert.IsType<LoanTaken>(Assert.Single(changes));
        Assert.Equal(500m, loanTaken.Amount);
    }

    [Fact]
    public void Run_Caps_A_Repay_Request_To_The_Actual_Debt()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.TakeLoan(300m);
        team.RequestLoanRepayment(1000m); // просит больше, чем реально должны

        var changes = VoluntaryLoanStep.Run(team);

        var loanRepaid = Assert.IsType<LoanRepaid>(Assert.Single(changes));
        Assert.Equal(300m, loanRepaid.Amount); // урезано до реального долга
    }

    [Fact]
    public void Run_Resolves_Repayment_Before_Take_So_A_New_Loan_Cant_Be_Repaid_The_Same_Turn()
    {
        // Порядок внутри шага (SPEC §4, §5.9): сначала погашение — по долгу ДО этого хода, потом
        // новый заём. Заём, взятый этим же ходом, не должен быть виден заявке на погашение того же хода.
        var (_, team) = TestGameConfig.StartSessionWithOneTeam(); // долга нет
        team.RequestLoanRepayment(200m);
        team.RequestLoan(500m);

        var changes = VoluntaryLoanStep.Run(team);

        Assert.Collection(
            changes,
            change => Assert.Equal(0m, Assert.IsType<LoanRepaid>(change).Amount), // гасить было нечего
            change => Assert.Equal(500m, Assert.IsType<LoanTaken>(change).Amount));
    }

    [Fact]
    public void Run_Emits_A_Zero_LoanRepaid_To_Clear_A_Stale_Request_When_There_Is_No_Debt()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam(); // долга нет
        team.RequestLoanRepayment(50m);

        var changes = VoluntaryLoanStep.Run(team);

        var loanRepaid = Assert.IsType<LoanRepaid>(Assert.Single(changes));
        Assert.Equal(0m, loanRepaid.Amount);
    }
}
