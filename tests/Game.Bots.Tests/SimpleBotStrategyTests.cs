using Game.Config.Loading;
using Game.Config.Session;
using Game.Domain;
using Game.Engine;

namespace Game.Bots.Tests;

/// <summary>
/// Две независимые оси стратегии (Блок 7.3.2, <c>docs/balancing-bots.md</c> §2): <c>leverage</c>
/// (толерантность к отрицательному балансу, docs/TODO.md #23) и <c>profile</c> (распределение усилий
/// по времени). Регрессионный ориентир — <c>leverage=1, profile=0</c> (значения по умолчанию) уже
/// покрыт существующими тестами <see cref="BotSessionRunnerTests"/>/<see cref="CrossSectorTradingTests"/>,
/// не дублируется здесь.
/// </summary>
public class SimpleBotStrategyTests
{
    /// <summary>
    /// <see cref="StartingConditionsConfig.MaxInitialBuildBudget"/>, урезанный так, что 0.25-доля
    /// (leverage=0) не покрывает даже самую дешёвую фабрику сектора А (iron-mine, 500) — на leverage=0
    /// бот вообще ничего не строит; полная доля (leverage=1, 1000) покрывает только её одну, не
    /// вторую (steel-mill, 1500) — та же логика больше не выбор «сколько занять», а толерантность к
    /// минусу (docs/TODO.md #23).
    /// </summary>
    private static ResolvedGameConfig WithTightBuildBudget(ResolvedGameConfig config) => new(
        config.Raw with { StartingConditions = new StartingConditionsConfig { MaxInitialBuildBudget = 1000m } },
        config.Sectors, config.Materials, config.RecipeBook, config.FactoryDefinitions);

    [Fact]
    public void BuildOutSectorChain_Builds_Nothing_At_Zero_Leverage_When_The_Budget_Cant_Cover_Even_The_Cheapest_Factory()
    {
        var config = WithTightBuildBudget(PilotBotSession.LoadConfig());
        var sectorA = config.Sectors.Single(s => s.Id == "A");

        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config, "short", endTurn: 15, new[] { new TeamSpec { Id = teamId, Name = "Бот", SectorId = sectorA.Id } });
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement(1) -> Decision(1)

        var bot = new SimpleBot(teamId, sectorA, config, leverage: 0m);
        bot.BuildOutSectorChain(session);

        Assert.Empty(session.State.Teams[teamId].Factories);
    }

    [Fact]
    public void BuildOutSectorChain_Builds_Only_What_Fits_The_Budget_At_Full_Leverage()
    {
        var config = WithTightBuildBudget(PilotBotSession.LoadConfig());
        var sectorA = config.Sectors.Single(s => s.Id == "A");

        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config, "short", endTurn: 15, new[] { new TeamSpec { Id = teamId, Name = "Бот", SectorId = sectorA.Id } });
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement(1) -> Decision(1)

        var bot = new SimpleBot(teamId, sectorA, config, leverage: 1m);
        bot.BuildOutSectorChain(session);

        // Бюджет 1000 покрывает iron-mine (500), но не хватает вдобавок на steel-mill (1500) —
        // достройка не бросает исключение и не строит частично оплаченную фабрику, просто
        // откладывает её до следующего хода решений (когда баланс подрастёт продажами).
        var built = Assert.Single(session.State.Teams[teamId].Factories);
        Assert.Equal("iron-mine", built.Definition.Id);
    }

    [Fact]
    public void UpdateInvestmentPace_Invests_From_The_First_Turn_For_A_Front_Loaded_Profile()
    {
        var config = PilotBotSession.LoadConfig();
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config, "short", endTurn: 15, new[] { new TeamSpec { Id = teamId, Name = "Бот", SectorId = sectorA.Id } });
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement(1) -> Decision(1)

        var bot = new SimpleBot(teamId, sectorA, config, leverage: 1m, profile: 0m);
        bot.BuildOutSectorChain(session);
        bot.UpdateInvestmentPace(session);

        var team = session.State.Teams[teamId];
        Assert.Equal(config.Raw.GenerationResearch.MaxCommitmentPerTurn, team.GenerationResearchCommitmentPerTurn);
        Assert.All(team.Factories, factory => Assert.Equal(config.Raw.Rnd.MaxCommitmentPerTurn, factory.RndCommitmentPerTurn));
    }

    [Fact]
    public void UpdateInvestmentPace_Holds_Zero_Investment_Until_The_Switch_Turn_For_A_Fully_Back_Loaded_Profile()
    {
        var config = PilotBotSession.LoadConfig();
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var maxTurns = config.Raw.SessionPresets.Single(p => p.Id == "short").MaxTurns;
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config, "short", endTurn: maxTurns, new[] { new TeamSpec { Id = teamId, Name = "Бот", SectorId = sectorA.Id } });

        // profile=1 -> момент переключения точно на последнем ходу пресета (см. doc-comment
        // UpdateInvestmentPace) — до него вложения нулевые, на нём и после — на потолок leverage.
        var bot = new SimpleBot(teamId, sectorA, config, leverage: 1m, profile: 1m);
        var random = new Random(1);
        var sawZeroBeforeSwitch = false;

        while (!session.State.IsFinished)
        {
            switch (session.State.CurrentPhase)
            {
                case TurnPhase.Settlement:
                    session.RunTick(random);
                    session.AdvancePhase(PhaseTransitionTrigger.Timer);
                    break;

                case TurnPhase.Decision:
                    if (session.State.CurrentTurn == 1)
                    {
                        bot.BuildOutSectorChain(session);
                    }
                    bot.BuildNewlyUnlockedFactories(session);
                    bot.UpdateInvestmentPace(session);

                    var team = session.State.Teams[teamId];
                    if (session.State.CurrentTurn < maxTurns)
                    {
                        sawZeroBeforeSwitch |= team.GenerationResearchCommitmentPerTurn == 0m;
                        Assert.Equal(0m, team.GenerationResearchCommitmentPerTurn);
                    }
                    else
                    {
                        Assert.Equal(config.Raw.GenerationResearch.MaxCommitmentPerTurn, team.GenerationResearchCommitmentPerTurn);
                    }

                    session.AdvancePhase(PhaseTransitionTrigger.Timer);
                    break;
            }
        }

        Assert.True(sawZeroBeforeSwitch, "Ни на одном ходу до переключения не был замечен нулевой темп вложений — тест ничего не проверил.");
    }

}
