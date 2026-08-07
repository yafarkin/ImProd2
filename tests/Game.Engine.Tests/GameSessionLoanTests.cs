namespace Game.Engine.Tests;

/// <summary>
/// Добровольный заём/погашение по решению команды через <see cref="GameSession"/> (SPEC §4, §5.9):
/// объявление на фазе решений мгновенно и бесплатно, реальное движение денег — на расчёте (<see
/// cref="VoluntaryLoanStep"/>), покрыто отдельно в <see cref="VoluntaryLoanStepTests"/>.
/// </summary>
public class GameSessionLoanTests
{
    private static (GameSession Session, Ulid TeamId) StartInDecisionPhase()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam(startingLoan: 0m);
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision

        return (session, teamId);
    }

    [Fact]
    public void TakeLoan_Appends_A_LoanTakeRequested_Event_Without_Touching_Debt_Or_Balance_Yet()
    {
        var (session, teamId) = StartInDecisionPhase();

        var entry = session.TakeLoan(teamId, 500m);

        var requested = Assert.IsType<LoanTakeRequested>(entry.Change);
        Assert.Equal(500m, requested.Amount);
        Assert.Equal(500m, session.State.Teams[teamId].PendingLoanTakeAmount);
        Assert.Equal(0m, session.State.Teams[teamId].Debt);
        Assert.Equal(0m, session.State.Teams[teamId].Balance);
    }

    [Fact]
    public void TakeLoan_Takes_Effect_Only_At_The_Next_RunTick()
    {
        var (session, teamId) = StartInDecisionPhase();
        session.TakeLoan(teamId, 500m);

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        var appended = session.RunTick(new Random(1));

        var loanTaken = Assert.IsType<LoanTaken>(Assert.Single(appended, e => e.Change is LoanTaken).Change);
        Assert.Equal(500m, loanTaken.Amount);
        Assert.Equal(500m, session.State.Teams[teamId].Debt);
        Assert.Equal(500m, session.State.Teams[teamId].Balance);
        Assert.Equal(0m, session.State.Teams[teamId].PendingLoanTakeAmount); // заявка снята
    }

    [Fact]
    public void TakeLoan_Twice_In_The_Same_Turn_Only_The_Last_Declaration_Counts()
    {
        var (session, teamId) = StartInDecisionPhase();
        session.TakeLoan(teamId, 500m);
        session.TakeLoan(teamId, 300m); // передумали — заявки не суммируются

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        session.RunTick(new Random(1));

        Assert.Equal(300m, session.State.Teams[teamId].Debt);
    }

    [Fact]
    public void TakeLoan_Zero_Cancels_A_Pending_Request()
    {
        var (session, teamId) = StartInDecisionPhase();
        session.TakeLoan(teamId, 500m);
        session.TakeLoan(teamId, 0m); // передумали совсем

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        var appended = session.RunTick(new Random(1));

        Assert.DoesNotContain(appended, e => e.Change is LoanTaken);
        Assert.Equal(0m, session.State.Teams[teamId].Debt);
    }

    [Fact]
    public void TakeLoan_Throws_For_An_Unknown_Team()
    {
        var (session, _) = StartInDecisionPhase();

        Assert.Throws<ArgumentException>(() => session.TakeLoan(Ulid.NewUlid(), 500m));
    }

    [Fact]
    public void TakeLoan_Throws_For_A_Negative_Amount()
    {
        var (session, teamId) = StartInDecisionPhase();

        Assert.Throws<ArgumentOutOfRangeException>(() => session.TakeLoan(teamId, -1m));
    }

    [Fact]
    public void TakeLoan_Throws_Outside_The_Decision_Phase()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam(startingLoan: 0m); // Settlement, ход 1

        Assert.Throws<InvalidOperationException>(() => session.TakeLoan(teamId, 500m));
    }

    [Fact]
    public void TakeLoan_Increases_The_Effective_Rate_Seen_By_The_Interest_Charge_After_It_Settles()
    {
        var (session, teamId) = StartInDecisionPhase();
        session.TakeLoan(teamId, 1000m); // ставка = BaseLoanInterestRate + LoanInterestRateGrowthPerUnitBorrowed * 1000

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        session.RunTick(new Random(1)); // заём зачисляется здесь, самым последним шагом — проценты этого тика его ещё не видят

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 3
        var appended = session.RunTick(new Random(1));

        var interest = Assert.IsType<LoanInterestCharged>(appended.Single(e => e.Change is LoanInterestCharged).Change);
        var loanConfig = TestGameConfig.Resolved.Raw.StartingConditions;
        var expectedRate = FinanceCalculator.CalculateEffectiveLoanRate(1000m, 0m, 100m, loanConfig);
        Assert.Equal(expectedRate, interest.Rate);
    }

    [Fact]
    public void RepayLoan_Appends_A_LoanRepaymentRequested_Event_Without_Touching_Debt_Or_Balance_Yet()
    {
        var (session, teamId) = StartInDecisionPhase();
        session.TakeLoan(teamId, 500m);
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        session.RunTick(new Random(1)); // заём зачислён, Debt=500
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision

        var entry = session.RepayLoan(teamId, 200m);

        var requested = Assert.IsType<LoanRepaymentRequested>(entry.Change);
        Assert.Equal(200m, requested.Amount);
        Assert.Equal(200m, session.State.Teams[teamId].PendingLoanRepayAmount);
        Assert.Equal(500m, session.State.Teams[teamId].Debt); // не изменилось сразу
    }

    [Fact]
    public void RepayLoan_Takes_Effect_Only_At_The_Next_RunTick()
    {
        var (session, teamId) = StartInDecisionPhase();
        session.TakeLoan(teamId, 500m);
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        session.RunTick(new Random(1));
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision
        session.RepayLoan(teamId, 200m);

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 3
        var appended = session.RunTick(new Random(1));

        var loanRepaid = Assert.IsType<LoanRepaid>(Assert.Single(appended, e => e.Change is LoanRepaid).Change);
        Assert.Equal(200m, loanRepaid.Amount);
        Assert.Equal(300m, session.State.Teams[teamId].Debt);
        Assert.Equal(0m, session.State.Teams[teamId].PendingLoanRepayAmount);
    }

    [Fact]
    public void RepayLoan_Clamps_An_Amount_Above_The_Actual_Debt_At_Settlement_Time_Instead_Of_Throwing()
    {
        // Баг-репорт пользователя (сохранён из немедленной версии): UI округляет долг для отображения
        // («1 ¤» вместо реальных 0.9966...) — попытка погасить ровно показанное на экране раньше
        // падала с исключением. Команда явно имела в виду «закрыть долг полностью». Теперь урезание
        // происходит на расчёте, по фактическому долгу на тот момент, а не по значению, которое было
        // видно в момент решения.
        var (session, teamId) = StartInDecisionPhase();
        session.TakeLoan(teamId, 500m);
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        session.RunTick(new Random(1));
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision
        // Небольшой грант сверху (Блок 9.6) — без него на балансе не хватило бы ровно на проценты
        // этого хода (25 = 500 * 0.05) поверх полного погашения долга, и недостачу закрыл бы
        // принудительный заём вместо чистого «Debt=0» — отдельный, более интересный сценарий, не
        // то, что здесь проверяется.
        session.GrantToTeam(teamId, 50m);
        session.RepayLoan(teamId, 500.01m);

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 3
        var appended = session.RunTick(new Random(1));

        var loanRepaid = Assert.IsType<LoanRepaid>(Assert.Single(appended, e => e.Change is LoanRepaid).Change);
        Assert.Equal(500m, loanRepaid.Amount); // урезано до реального остатка долга
        Assert.Equal(0m, session.State.Teams[teamId].Debt);
        Assert.DoesNotContain(appended, e => e.Change is ForcedLoanTaken);
    }

    [Fact]
    public void RepayLoan_That_Leaves_No_Cash_For_The_Same_Turns_Interest_Triggers_A_Forced_Loan()
    {
        // Не баг, а прямое следствие порядка внутри тика (SPEC §4, §5.9): проценты списываются в
        // начале, добровольное погашение — в конце, и оба этого хода видят один и тот же баланс. Если
        // на полное погашение уходит вся сумма займа, а проценты этого хода откусили от неё раньше, —
        // недостачу закрывает принудительный заём, тем же способом, каким уже покрывается любая
        // другая дыра к концу тика.
        var (session, teamId) = StartInDecisionPhase();
        session.TakeLoan(teamId, 500m);
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        session.RunTick(new Random(1));
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision
        session.RepayLoan(teamId, 500m); // без гранта-подушки, в отличие от теста выше

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 3
        var appended = session.RunTick(new Random(1));

        var loanRepaid = Assert.IsType<LoanRepaid>(Assert.Single(appended, e => e.Change is LoanRepaid).Change);
        Assert.Equal(500m, loanRepaid.Amount);
        var forcedLoan = Assert.IsType<ForcedLoanTaken>(Assert.Single(appended, e => e.Change is ForcedLoanTaken).Change);
        Assert.Equal(25m, forcedLoan.Amount); // ровно проценты этого хода (500 * 0.05), которые погашение не оставило чем покрыть
        Assert.Equal(25m, session.State.Teams[teamId].Debt); // долг закрыт и тут же переоткрыт принудительным займом
        Assert.Equal(0m, session.State.Teams[teamId].Balance);
    }

    [Fact]
    public void RepayLoan_Quietly_Resolves_To_Nothing_When_The_Team_Has_No_Debt_At_Settlement_Time()
    {
        // В отличие от немедленной версии — больше не бросает при заявке. Долг на момент расчёта
        // может отличаться от того, что было видно в момент решения, поэтому проверка отложена туда же.
        var (session, teamId) = StartInDecisionPhase(); // без TakeLoan — долга вообще нет

        session.RepayLoan(teamId, 10m);
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        var appended = session.RunTick(new Random(1));

        var loanRepaid = Assert.IsType<LoanRepaid>(Assert.Single(appended, e => e.Change is LoanRepaid).Change);
        Assert.Equal(0m, loanRepaid.Amount);
        Assert.Equal(0m, session.State.Teams[teamId].Debt);
        Assert.Equal(0m, session.State.Teams[teamId].PendingLoanRepayAmount); // заявка снята, не висит на будущее
    }

    [Fact]
    public void RepayLoan_Zero_Cancels_A_Pending_Request()
    {
        var (session, teamId) = StartInDecisionPhase();
        session.TakeLoan(teamId, 500m);
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        session.RunTick(new Random(1));
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision
        session.RepayLoan(teamId, 200m);
        session.RepayLoan(teamId, 0m); // передумали совсем

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 3
        var appended = session.RunTick(new Random(1));

        Assert.DoesNotContain(appended, e => e.Change is LoanRepaid);
        Assert.Equal(500m, session.State.Teams[teamId].Debt);
    }

    [Fact]
    public void RepayLoan_Throws_For_An_Unknown_Team()
    {
        var (session, _) = StartInDecisionPhase();

        Assert.Throws<ArgumentException>(() => session.RepayLoan(Ulid.NewUlid(), 100m));
    }

    [Fact]
    public void RepayLoan_Throws_For_A_Negative_Amount()
    {
        var (session, teamId) = StartInDecisionPhase();
        session.TakeLoan(teamId, 500m);

        Assert.Throws<ArgumentOutOfRangeException>(() => session.RepayLoan(teamId, -1m));
    }

    [Fact]
    public void RepayLoan_Throws_Outside_The_Decision_Phase()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam(startingLoan: 500m); // Settlement, ход 1

        Assert.Throws<InvalidOperationException>(() => session.RepayLoan(teamId, 100m));
    }
}
