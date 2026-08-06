namespace Game.Engine.Tests;

/// <summary>
/// Объявление R&amp;D-обязательства на фабрику через <see cref="GameSession"/> (Блок 9.2, SPEC §5.8).
/// Само действие декларативное — только меняет <see cref="Factory.RndCommitmentPerTurn"/>, без
/// немедленного списания баланса или изменения уровня; реальное списание и рост уровня проверяются
/// отдельно, в тестах на <see cref="TickFinanceStep"/> (см. TickFinanceStepTests).
/// </summary>
public class GameSessionRndInvestmentTests
{
    // TestGameConfig.Resolved.Raw.Rnd.MaxCommitmentPerTurn == 200m.
    private static (GameSession Session, Ulid TeamId, Ulid FactoryId) StartInDecisionPhaseWithFactory()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision
        var built = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mine.Id).Change;

        return (session, teamId, built.FactoryId);
    }

    [Fact]
    public void SetRndCommitment_Appends_RndCommitmentSet_And_Updates_The_Factory()
    {
        var (session, teamId, factoryId) = StartInDecisionPhaseWithFactory();

        var entry = session.SetRndCommitment(teamId, factoryId, 50m);

        var commitmentSet = Assert.IsType<RndCommitmentSet>(entry.Change);
        Assert.Equal(50m, commitmentSet.Amount);
        Assert.Equal(50m, session.State.Teams[teamId].Factories.Single().RndCommitmentPerTurn);
    }

    [Fact]
    public void SetRndCommitment_Does_Not_Charge_The_Balance_Or_Advance_The_Level()
    {
        var (session, teamId, factoryId) = StartInDecisionPhaseWithFactory();
        var balanceBefore = session.State.Teams[teamId].Balance;

        session.SetRndCommitment(teamId, factoryId, 200m); // покрыло бы порог, будь это разовое вложение

        Assert.Equal(balanceBefore, session.State.Teams[teamId].Balance);
        Assert.Equal(0m, session.State.Teams[teamId].Factories.Single().RndInvestment);
        Assert.Equal(1, session.State.Teams[teamId].Factories.Single().Level);
    }

    [Fact]
    public void SetRndCommitment_Allows_Setting_Back_To_Zero()
    {
        var (session, teamId, factoryId) = StartInDecisionPhaseWithFactory();
        session.SetRndCommitment(teamId, factoryId, 100m);

        session.SetRndCommitment(teamId, factoryId, 0m);

        Assert.Equal(0m, session.State.Teams[teamId].Factories.Single().RndCommitmentPerTurn);
    }

    [Fact]
    public void SetRndCommitment_Throws_For_An_Unknown_Team()
    {
        var (session, _, factoryId) = StartInDecisionPhaseWithFactory();

        Assert.Throws<ArgumentException>(() => session.SetRndCommitment(Ulid.NewUlid(), factoryId, 50m));
    }

    [Fact]
    public void SetRndCommitment_Throws_For_An_Unknown_Factory()
    {
        var (session, teamId, _) = StartInDecisionPhaseWithFactory();

        Assert.Throws<ArgumentException>(() => session.SetRndCommitment(teamId, Ulid.NewUlid(), 50m));
    }

    [Fact]
    public void SetRndCommitment_Throws_For_A_Negative_Amount()
    {
        var (session, teamId, factoryId) = StartInDecisionPhaseWithFactory();

        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetRndCommitment(teamId, factoryId, -1m));
    }

    [Fact]
    public void SetRndCommitment_Throws_When_The_Amount_Exceeds_The_Per_Turn_Cap()
    {
        var (session, teamId, factoryId) = StartInDecisionPhaseWithFactory();
        var maxCommitmentPerTurn = TestGameConfig.Resolved.Raw.Rnd.MaxCommitmentPerTurn;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.SetRndCommitment(teamId, factoryId, maxCommitmentPerTurn + 1m));
    }

    [Fact]
    public void SetRndCommitment_Throws_Outside_The_Decision_Phase()
    {
        var (session, teamId, factoryId) = StartInDecisionPhaseWithFactory();
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement

        Assert.Throws<InvalidOperationException>(() => session.SetRndCommitment(teamId, factoryId, 50m));
    }
}
