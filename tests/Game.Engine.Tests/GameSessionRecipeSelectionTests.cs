using Game.Config.Session;

namespace Game.Engine.Tests;

/// <summary>Переключение продукта (рецепта) фабрики через <see cref="GameSession"/> (Блок 9.1, SPEC §9.3).</summary>
public class GameSessionRecipeSelectionTests
{
    private static (GameSession Session, Ulid TeamId) StartInDecisionPhaseWithSecondMillRecipe()
    {
        var config = TestGameConfig.BuildWithSecondMillRecipe();
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config,
            "test",
            endTurn: 999,
            new[]
            {
                new TeamSpec { Id = teamId, Name = "Команда А1", SectorId = TestGameConfig.SectorA.Id },
            });
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Calculation -> Decision

        return (session, teamId);
    }

    [Fact]
    public void SelectRecipe_Switches_The_Factorys_Selected_Recipe()
    {
        var (session, teamId) = StartInDecisionPhaseWithSecondMillRecipe();
        var built = (FactoryBuilt)session.BuildFactory(teamId, "steel-mill").Change;
        Assert.Equal("sheet-from-ore", session.State.Teams[teamId].Factories.Single().SelectedRecipe.Id);

        var entry = session.SelectRecipe(teamId, built.FactoryId, "wire-from-ore");

        var selected = Assert.IsType<RecipeSelected>(entry.Change);
        Assert.Equal("wire-from-ore", selected.RecipeId);
        Assert.Equal("wire-from-ore", session.State.Teams[teamId].Factories.Single().SelectedRecipe.Id);
    }

    [Fact]
    public void SelectRecipe_Throws_For_An_Unknown_Factory()
    {
        var (session, teamId) = StartInDecisionPhaseWithSecondMillRecipe();

        Assert.Throws<ArgumentException>(() => session.SelectRecipe(teamId, Ulid.NewUlid(), "wire-from-ore"));
    }

    [Fact]
    public void SelectRecipe_Throws_For_A_Recipe_Not_Produced_By_This_Factory_Definition()
    {
        var (session, teamId) = StartInDecisionPhaseWithSecondMillRecipe();
        var built = (FactoryBuilt)session.BuildFactory(teamId, "steel-mill").Change;

        Assert.Throws<ArgumentException>(() => session.SelectRecipe(teamId, built.FactoryId, "ore-mining"));
    }

    [Fact]
    public void SelectRecipe_Throws_Outside_The_Decision_Phase()
    {
        var config = TestGameConfig.BuildWithSecondMillRecipe();
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config,
            "test",
            endTurn: 999,
            new[]
            {
                new TeamSpec { Id = teamId, Name = "Команда А1", SectorId = TestGameConfig.SectorA.Id },
            });
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Calculation -> Decision
        var built = (FactoryBuilt)session.BuildFactory(teamId, "steel-mill").Change;
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Closing

        Assert.Throws<InvalidOperationException>(() => session.SelectRecipe(teamId, built.FactoryId, "wire-from-ore"));
    }
}
