namespace Game.Bots.Llm.Tests;

/// <summary>
/// Проверяет блок готовых показателей с трендом (запрос пользователя 2026-08-16: «маленькие модели
/// плохо считают, но могут легче рассуждать над готовыми цифрами»), на реальной сессии, без
/// обращения к LLM.
/// </summary>
public sealed class BotDerivedMetricsBuilderTests
{
    [Fact]
    public void Build_OnFirstTurn_HasNoPriorWindowAndEmptySections()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();

        var metrics = BotDerivedMetricsBuilder.Build(session, teamId);

        Assert.Contains("not enough history yet for a prior window", metrics);
        Assert.Contains("LOAN SERVICE", metrics);
        Assert.Contains("CASH FLOW", metrics);
        Assert.Contains("WAREHOUSE OVERAGE FEE", metrics);
        Assert.Contains("IDLE / UNDERPERFORMING FACTORIES", metrics);
        Assert.Contains("(no factories yet)", metrics);
        Assert.Contains("FACTORY UTILIZATION", metrics);
        Assert.Contains("R&D", metrics);
        Assert.Contains("RUNWAY", metrics);
        Assert.Contains("MARKET POSITION", metrics);
        Assert.Contains("You are currently the net worth leader", metrics);
    }

    [Fact]
    public void Build_UnknownTeam_Throws()
    {
        var (session, _) = TestSession.StartSingleTeamSession();

        Assert.Throws<ArgumentException>(() => BotDerivedMetricsBuilder.Build(session, Ulid.NewUlid()));
    }

    [Fact]
    public void Build_NonPositiveWindow_Throws()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();

        Assert.Throws<ArgumentOutOfRangeException>(() => BotDerivedMetricsBuilder.Build(session, teamId, windowSize: 0));
    }

    [Fact]
    public void Build_AfterSeveralTurns_HasPriorWindowInHeader()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var random = new Random(1);

        for (var i = 0; i < 6; i++)
        {
            TestSession.SettleOneTurn(session, random);
        }

        var metrics = BotDerivedMetricsBuilder.Build(session, teamId, windowSize: 5);

        // Ход сейчас 7: recent = ходы 3-7, prior = ходы 1-2 (обрезано снизу первым ходом).
        Assert.Contains("recent: turns 3-7, prior: turns 1-2", metrics);
    }

    [Fact]
    public void Build_FactoryWithNoWorkers_IsListedAsIdleWithReason()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        session.BuildFactory(teamId, "iron-mine");

        var metrics = BotDerivedMetricsBuilder.Build(session, teamId);

        Assert.Contains("iron-mine", metrics);
        Assert.Contains("no workers assigned", metrics);
    }

    [Fact]
    public void Build_FullyStaffedFactoryWithoutInputs_IsNotIdleAndFullyUtilized()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var random = new Random(3);

        session.BuildFactory(teamId, "iron-mine");
        var factoryId = session.State.Teams[teamId].Factories[0].Id;
        session.SetWorkerCount(teamId, factoryId, 10);

        for (var i = 0; i < 3; i++)
        {
            TestSession.SettleOneTurn(session, random);
        }

        var metrics = BotDerivedMetricsBuilder.Build(session, teamId);

        Assert.Contains("(none — all factories running at or near capacity)", metrics);
        Assert.Contains("- iron-mine (factoryId=" + factoryId + "): 100%", metrics);
    }

    [Fact]
    public void Build_AfterLoanAndInterestAccrues_LoanServiceShowsNonZeroInterest()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var random = new Random(5);

        session.TakeLoan(teamId, 1000m);
        for (var i = 0; i < 3; i++)
        {
            TestSession.SettleOneTurn(session, random);
        }

        var metrics = BotDerivedMetricsBuilder.Build(session, teamId);

        Assert.True(session.State.Teams[teamId].Debt > 0, "Precondition: team should still owe money for interest to have accrued.");
        Assert.Matches(@"Interest paid: (?!0\.00)\d", metrics);
    }

    [Fact]
    public void Build_TeamBehindOnNetWorth_ShowsLeaderAndOwnPosition()
    {
        var (session, teamAId, teamBId) = TestSession.StartTwoTeamSession();
        var random = new Random(11);

        session.TakeLoan(teamBId, 1000m);
        for (var i = 0; i < 4; i++)
        {
            TestSession.SettleOneTurn(session, random);
        }

        Assert.True(
            session.State.Teams[teamBId].Balance - session.State.Teams[teamBId].Debt <
            session.State.Teams[teamAId].Balance - session.State.Teams[teamAId].Debt,
            "Precondition: the borrowing team should have fallen behind after interest.");

        var leaderMetrics = BotDerivedMetricsBuilder.Build(session, teamAId);
        Assert.Contains("You are currently the net worth leader", leaderMetrics);

        var laggingMetrics = BotDerivedMetricsBuilder.Build(session, teamBId);
        Assert.Contains("leader (Команда А)", laggingMetrics);
    }
}
