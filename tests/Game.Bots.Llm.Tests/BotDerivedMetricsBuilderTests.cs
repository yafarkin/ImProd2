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
        Assert.Contains("LOAN COST RIGHT NOW", metrics);
        Assert.Contains("No debt", metrics);
        Assert.Contains("CASH FLOW", metrics);
        Assert.Contains("WAREHOUSE OVERAGE FEE", metrics);
        Assert.Contains("IDLE / UNDERPERFORMING FACTORIES", metrics);
        Assert.Contains("(no factories yet)", metrics);
        Assert.Contains("FACTORY WEAR", metrics);
        Assert.Contains("FACTORY UTILIZATION", metrics);
        Assert.Contains("R&D", metrics);
        Assert.Contains("RUNWAY", metrics);
        Assert.Contains("MARKET POSITION", metrics);
        Assert.Contains("You are currently the net worth leader", metrics);
    }

    [Fact]
    public void Build_WithDebt_ShowsTheExactRateAndNextTurnInterestCost()
    {
        // Прямой запрос пользователя 2026-08-20, по следам _2bot_gpt_oss_20b_2stage_v2: боту нужна
        // конкретная цена долга ЧИСЛОМ, не абстрактное "ставка растёт" — иначе он берёт заём на заём
        // не замечая, что это уже дорого.
        var (session, teamId) = TestSession.StartSingleTeamSession();
        session.TakeLoan(teamId, 100_000m);
        TestSession.SettleOneTurn(session, new Random(1));

        var metrics = BotDerivedMetricsBuilder.Build(session, teamId);

        Assert.Contains("LOAN COST RIGHT NOW", metrics);
        Assert.Contains("Effective rate:", metrics);
        Assert.Contains("interest NEXT turn", metrics);
        Assert.DoesNotContain("No debt", metrics);
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
    public void Build_FreshFactory_IsNotFlaggedAsWearRisk()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        session.BuildFactory(teamId, "iron-mine");

        var metrics = BotDerivedMetricsBuilder.Build(session, teamId);

        Assert.Contains("(none — every factory is either healthy or already has an overhaul requested)", metrics);
    }

    [Fact]
    public void Build_FactoryLeftToDecayWithoutOverhaul_IsFlaggedAsWearRisk()
    {
        // Запрос пользователя 2026-08-19, по следам живого прогона (docs/bot-runs/2026-08-19-stage1-
        // gpt-oss-20b/ANALYSIS.md): бот ни разу не вызвал setOverhaulRequested за 90 ходов, износ
        // копился незаметно, несколько фабрик разом ушли в вынужденный простой. Турн-калибровка —
        // на пилотном конфиге (GracePeriodTurns=8, BaseWearRatePerTurn=0.01, AccelerationFactorPerTurn=
        // 0.004): condition пересекает предупредительный порог (2×CriticalConditionThreshold=0.4) на
        // ходу 24 (0.370), вынужденный ремонт (порог 0.2) — не раньше хода 27; 23 хода settlement —
        // безопасно внутри окна «уже низко, но ещё не поздно попросить капремонт самому».
        var (session, teamId) = TestSession.StartSingleTeamSession(endTurn: 30);
        var random = new Random(13);
        session.BuildFactory(teamId, "iron-mine");
        var factoryId = session.State.Teams[teamId].Factories[0].Id;
        session.SetWorkerCount(teamId, factoryId, 1);

        for (var i = 0; i < 23; i++)
        {
            TestSession.SettleOneTurn(session, random);
        }

        var factory = session.State.Teams[teamId].Factories[0];
        Assert.True(factory.Condition < 0.4m, $"Precondition: expected condition below the warn threshold, was {factory.Condition}.");
        Assert.False(factory.IsUnderRepair, "Precondition: factory should not be forced into repair yet.");

        var metrics = BotDerivedMetricsBuilder.Build(session, teamId);

        Assert.Contains("no overhaul requested", metrics);
        Assert.Contains("call setOverhaulRequested now", metrics);
    }

    [Fact]
    public void Build_LowConditionFactoryWithOverhaulAlreadyRequested_IsNotFlaggedAgain()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession(endTurn: 30);
        var random = new Random(13);
        session.BuildFactory(teamId, "iron-mine");
        var factoryId = session.State.Teams[teamId].Factories[0].Id;
        session.SetWorkerCount(teamId, factoryId, 1);

        for (var i = 0; i < 23; i++)
        {
            TestSession.SettleOneTurn(session, random);
        }

        session.SetOverhaulRequested(teamId, factoryId, true);

        var metrics = BotDerivedMetricsBuilder.Build(session, teamId);

        Assert.Contains("(none — every factory is either healthy or already has an overhaul requested)", metrics);
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
