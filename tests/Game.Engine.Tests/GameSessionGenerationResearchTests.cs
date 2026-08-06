namespace Game.Engine.Tests;

/// <summary>
/// Объявление командного исследования следующего поколения фабрик через <see cref="GameSession"/>
/// (Блок 9.2) — декларативное действие, зеркальное <see cref="GameSession.SetRndCommitment"/>, но
/// на уровне команды. Само действие только меняет <see cref="Team.GenerationResearchCommitmentPerTurn"/>,
/// без немедленного списания баланса или изменения поколения; реальное списание и переход поколения
/// проверяются отдельно, в TickFinanceStepGenerationResearchTests.
/// </summary>
public class GameSessionGenerationResearchTests
{
    private static (GameSession Session, Ulid TeamId) StartInDecisionPhase()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision

        return (session, teamId);
    }

    [Fact]
    public void SetGenerationResearchCommitment_Appends_The_Event_And_Updates_The_Team()
    {
        var (session, teamId) = StartInDecisionPhase();

        var entry = session.SetGenerationResearchCommitment(teamId, 50m);

        var commitmentSet = Assert.IsType<GenerationResearchCommitmentSet>(entry.Change);
        Assert.Equal(50m, commitmentSet.Amount);
        Assert.Equal(50m, session.State.Teams[teamId].GenerationResearchCommitmentPerTurn);
    }

    [Fact]
    public void SetGenerationResearchCommitment_Does_Not_Charge_The_Balance_Or_Advance_The_Generation()
    {
        var (session, teamId) = StartInDecisionPhase();
        var balanceBefore = session.State.Teams[teamId].Balance;

        session.SetGenerationResearchCommitment(teamId, 300m);

        Assert.Equal(balanceBefore, session.State.Teams[teamId].Balance);
        Assert.Equal(0m, session.State.Teams[teamId].GenerationResearchInvestment);
        Assert.Equal(1, session.State.Teams[teamId].UnlockedGeneration);
    }

    [Fact]
    public void SetGenerationResearchCommitment_Allows_Setting_Back_To_Zero()
    {
        var (session, teamId) = StartInDecisionPhase();
        session.SetGenerationResearchCommitment(teamId, 100m);

        session.SetGenerationResearchCommitment(teamId, 0m);

        Assert.Equal(0m, session.State.Teams[teamId].GenerationResearchCommitmentPerTurn);
    }

    [Fact]
    public void SetGenerationResearchCommitment_Throws_For_An_Unknown_Team()
    {
        var (session, _) = StartInDecisionPhase();

        Assert.Throws<ArgumentException>(() => session.SetGenerationResearchCommitment(Ulid.NewUlid(), 50m));
    }

    [Fact]
    public void SetGenerationResearchCommitment_Throws_For_A_Negative_Amount()
    {
        var (session, teamId) = StartInDecisionPhase();

        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetGenerationResearchCommitment(teamId, -1m));
    }

    [Fact]
    public void SetGenerationResearchCommitment_Throws_When_The_Amount_Exceeds_The_Per_Turn_Cap()
    {
        var (session, teamId) = StartInDecisionPhase();
        var maxCommitmentPerTurn = TestGameConfig.Resolved.Raw.GenerationResearch.MaxCommitmentPerTurn;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.SetGenerationResearchCommitment(teamId, maxCommitmentPerTurn + 1m));
    }

    [Fact]
    public void SetGenerationResearchCommitment_Throws_Outside_The_Decision_Phase()
    {
        var (session, teamId) = StartInDecisionPhase();
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement

        Assert.Throws<InvalidOperationException>(() => session.SetGenerationResearchCommitment(teamId, 50m));
    }
}
