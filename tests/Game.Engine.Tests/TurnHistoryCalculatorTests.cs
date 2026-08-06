namespace Game.Engine.Tests;

/// <summary>Ретроспективная сводка по ходам для дебрифа (Блок 10.1, SPEC §12).</summary>
public class TurnHistoryCalculatorTests
{
    [Fact]
    public void Summarize_Returns_A_Row_For_The_Current_Turn_Even_Before_It_Ends()
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam(startingLoan: 500m);

        var summary = TurnHistoryCalculator.Summarize(session.Entries, session.State.Config);

        var row = Assert.Single(summary);
        Assert.Equal(1, row.Turn);
        Assert.Equal(500m, row.TotalCash);
        Assert.Equal(0m, row.VolumeSoldToSystem);
        Assert.Equal(0, row.ForcedLoanCount);
    }

    /// <summary>
    /// Реплей <see cref="TurnHistoryCalculator"/> идёт по своей отдельной копии состояния — склад,
    /// зачисленный тестом напрямую в живой <see cref="Team"/> (как делают другие тесты движка,
    /// например <c>GameSessionMarketTests</c>), в эту копию не попадёт. Поэтому здесь товар
    /// зачисляется тем же журналируемым событием (<see cref="EmergencyPurchased"/>), что и в
    /// реальной сессии, — весь сценарий собирается через <c>EventLog.Append</c> напрямую.
    /// </summary>
    [Fact]
    public void Summarize_Attributes_A_Sale_To_The_Turn_It_Happened_In_And_Starts_A_Fresh_Row_Next_Turn()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        log.Append(new EmergencyPurchased { Id = Ulid.NewUlid(), Turn = 1, TeamId = team.Id, MaterialId = "ore", Volume = 20m, UnitPrice = 10m, TotalCost = 200m });
        log.Append(new MaterialSoldToSystem
        {
            Id = Ulid.NewUlid(), TeamId = team.Id, MaterialId = "ore", Volume = 20m,
            WithinCapacityVolume = 20m, OverflowVolume = 0m, UnitPrice = 10m, TotalRevenue = 200m,
        });
        log.Append(new PhaseAdvanced { Id = Ulid.NewUlid(), Trigger = PhaseTransitionTrigger.Timer }); // Settlement -> Decision
        log.Append(new PhaseAdvanced { Id = Ulid.NewUlid(), Trigger = PhaseTransitionTrigger.Timer }); // Decision -> Settlement, ход 2

        var summary = TurnHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved);

        Assert.Equal(2, summary.Count);
        Assert.Equal(1, summary[0].Turn);
        Assert.Equal(20m, summary[0].VolumeSoldToSystem);
        Assert.Equal(2, summary[1].Turn);
        Assert.Equal(0m, summary[1].VolumeSoldToSystem);
    }

    [Fact]
    public void Summarize_Counts_Forced_Loans_In_The_Turn_They_Were_Taken()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        log.Append(new ForcedLoanTaken { Id = Ulid.NewUlid(), TeamId = team.Id, Amount = 80m, NewPenaltyRateSurcharge = 0.1m });

        var summary = TurnHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved);

        var row = Assert.Single(summary);
        Assert.Equal(1, row.ForcedLoanCount);
    }
}
