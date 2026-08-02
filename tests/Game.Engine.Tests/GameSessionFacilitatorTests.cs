namespace Game.Engine.Tests;

/// <summary>Экраны ведущего — управление фазой, грант, аварийная закупка, корректировка цены (Блок 9.6, SPEC §9.5).</summary>
public class GameSessionFacilitatorTests
{
    [Fact]
    public void AdvancePhase_With_FacilitatorTrigger_Advances_Immediately()
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam(); // Settlement, ход 1

        session.AdvancePhase(PhaseTransitionTrigger.Facilitator);

        Assert.Equal(TurnPhase.Decision, session.State.CurrentPhase);
    }

    [Fact]
    public void Pause_Then_Resume_Round_Trips_IsPaused()
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam();

        session.Pause();
        Assert.True(session.State.IsPaused);

        session.Resume();
        Assert.False(session.State.IsPaused);
    }

    [Fact]
    public void Pause_Throws_When_Already_Paused()
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam();
        session.Pause();

        Assert.Throws<InvalidOperationException>(() => session.Pause());
    }

    [Fact]
    public void ExtendCurrentPhase_Throws_For_A_NonPositive_Duration()
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam();

        Assert.Throws<ArgumentOutOfRangeException>(() => session.ExtendCurrentPhase(TimeSpan.Zero));
    }

    [Fact]
    public void GrantToTeam_Credits_Balance_Without_Increasing_Debt()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        var balanceBefore = session.State.Teams[teamId].Balance;
        var debtBefore = session.State.Teams[teamId].Debt;

        var entry = session.GrantToTeam(teamId, 500m);

        Assert.IsType<GrantIssued>(entry.Change);
        Assert.Equal(balanceBefore + 500m, session.State.Teams[teamId].Balance);
        Assert.Equal(debtBefore, session.State.Teams[teamId].Debt);
    }

    [Fact]
    public void GrantToTeam_Throws_For_A_NonPositive_Amount()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();

        Assert.Throws<ArgumentOutOfRangeException>(() => session.GrantToTeam(teamId, 0m));
    }

    [Fact]
    public void GrantToTeam_Throws_For_An_Unknown_Team()
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam();

        Assert.Throws<ArgumentException>(() => session.GrantToTeam(Ulid.NewUlid(), 100m));
    }

    [Fact]
    public void GrantToTeam_Works_Outside_The_Decision_Phase()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam(); // Settlement, ход 1

        var entry = session.GrantToTeam(teamId, 100m);

        Assert.IsType<GrantIssued>(entry.Change);
    }

    [Fact]
    public void SetEmergencyPurchaseEnabled_Toggles_The_Runtime_Flag()
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam();
        Assert.True(session.State.EmergencyPurchaseEnabled); // TestGameConfig: EmergencyPurchaseEnabled = true

        session.SetEmergencyPurchaseEnabled(false);

        Assert.False(session.State.EmergencyPurchaseEnabled);
    }

    [Fact]
    public void EmergencyPurchase_Respects_The_Toggled_Flag_Even_When_The_Config_Default_Is_Enabled()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision
        session.SetEmergencyPurchaseEnabled(false);

        Assert.Throws<InvalidOperationException>(() => session.EmergencyPurchase(teamId, "ore", 5m));
    }

    [Fact]
    public void AdjustMarketPrice_Replaces_The_Price_Preserving_Capacity()
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam();
        var capacityBefore = session.State.Market.QuoteOf("ore").Capacity;

        session.AdjustMarketPrice("ore", 42m);

        var quote = session.State.Market.QuoteOf("ore");
        Assert.Equal(42m, quote.Price);
        Assert.Equal(capacityBefore, quote.Capacity);
    }

    [Fact]
    public void AdjustMarketPrice_Throws_For_An_Unknown_Material()
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam();

        Assert.Throws<ArgumentException>(() => session.AdjustMarketPrice("no-such-material", 10m));
    }

    [Fact]
    public void AdjustMarketPrice_Throws_For_A_Negative_Price()
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam();

        Assert.Throws<ArgumentOutOfRangeException>(() => session.AdjustMarketPrice("ore", -1m));
    }
}
