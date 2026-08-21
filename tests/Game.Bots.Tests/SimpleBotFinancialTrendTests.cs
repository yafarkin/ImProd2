using Game.Domain;
using Game.Engine;

namespace Game.Bots.Tests;

/// <summary>
/// Финансовая осторожность (запрос пользователя — найдено первым калибровочным прогоном
/// `metallurgy.json`: бот раскручивал спираль принудительных займов, продолжая строить фабрики и
/// вкладывать в R&amp;D независимо от того, что кассовый разрыв только рос). См. doc-comment <see
/// cref="SimpleBot.UpdateFinancialTrend"/>.
/// </summary>
public class SimpleBotFinancialTrendTests
{
    [Fact]
    public void UpdateInvestmentPace_Keeps_The_Nominal_Ceiling_While_Net_Worth_Does_Not_Decline()
    {
        var config = PilotBotSession.LoadConfig();
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config, "short", endTurn: 15, new[] { new TeamSpec { Id = teamId, Name = "Бот", SectorId = sectorA.Id } });
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement(1) -> Decision(1)

        var bot = new SimpleBot(teamId, sectorA, config, leverage: 1m, profile: 0m);
        bot.BuildOutSectorChain(session);
        bot.UpdateFinancialTrend(session); // здоровый тренд — ничего не менялось
        bot.UpdateInvestmentPace(session);

        var team = session.State.Teams[teamId];
        Assert.Equal(config.Raw.GenerationResearch.MaxCommitmentPerTurn, team.GenerationResearchCommitmentPerTurn);
    }

    [Fact]
    public void UpdateInvestmentPace_Reduces_The_Ceiling_Fraction_Once_Net_Worth_Keeps_Declining()
    {
        var config = PilotBotSession.LoadConfig();
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config, "short", endTurn: 15, new[] { new TeamSpec { Id = teamId, Name = "Бот", SectorId = sectorA.Id } });
        session.AdvancePhase(PhaseTransitionTrigger.Timer);

        // leverage=1 -> DistressThresholdTurns=4 (терпит дольше, аппетит к риску) -> нужно 4 хода
        // ухудшения, чтобы порог сработал впервые, и ещё 3 хода подряд ухудшения, чтобы throttle
        // (шаг 0.25) дошёл до нуля целиком — 7 подряд деклайнов итого.
        var bot = new SimpleBot(teamId, sectorA, config, leverage: 1m, profile: 0m);
        bot.BuildOutSectorChain(session);
        bot.UpdateFinancialTrend(session);

        var team = session.State.Teams[teamId];
        for (var i = 0; i < 7; i++)
        {
            team.Debit(500m); // симулируем растущий кассовый разрыв без полного тика
            bot.UpdateFinancialTrend(session);
        }

        bot.UpdateInvestmentPace(session);

        Assert.Equal(0m, team.GenerationResearchCommitmentPerTurn); // throttle=0 -> доля 0 независимо от leverage
        Assert.All(team.Factories, factory => Assert.Equal(0m, factory.RndCommitmentPerTurn));
    }

    [Fact]
    public void UpdateInvestmentPace_Recovers_The_Nominal_Ceiling_Once_Net_Worth_Improves_Again()
    {
        var config = PilotBotSession.LoadConfig();
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config, "short", endTurn: 15, new[] { new TeamSpec { Id = teamId, Name = "Бот", SectorId = sectorA.Id } });
        session.AdvancePhase(PhaseTransitionTrigger.Timer);

        var bot = new SimpleBot(teamId, sectorA, config, leverage: 1m, profile: 0m);
        bot.BuildOutSectorChain(session);
        bot.UpdateFinancialTrend(session);

        var team = session.State.Teams[teamId];
        for (var i = 0; i < 7; i++)
        {
            team.Debit(500m);
            bot.UpdateFinancialTrend(session);
        }

        // Тренд развернулся — throttle плавно (шаг 0.25) возвращается к 1, здесь хватает с запасом.
        for (var i = 0; i < 4; i++)
        {
            team.Credit(1000m);
            bot.UpdateFinancialTrend(session);
        }

        bot.UpdateInvestmentPace(session);

        Assert.Equal(config.Raw.GenerationResearch.MaxCommitmentPerTurn, team.GenerationResearchCommitmentPerTurn);
    }

    [Fact]
    public void BuildNewlyUnlockedFactories_Pauses_New_Construction_While_In_Financial_Distress()
    {
        var config = PilotBotSession.LoadConfig();
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config, "short", endTurn: 15, new[] { new TeamSpec { Id = teamId, Name = "Бот", SectorId = sectorA.Id } });
        session.AdvancePhase(PhaseTransitionTrigger.Timer);

        // leverage=0 -> DistressThresholdTurns=1, throttle доходит до нуля за 4 хода подряд ухудшения
        // — самый быстрый случай, чтобы проверить именно достройку, не темп R&D.
        var bot = new SimpleBot(teamId, sectorA, config, leverage: 0m, profile: 0m);
        var team = session.State.Teams[teamId];

        bot.UpdateFinancialTrend(session); // ход 1: Balance=Debt=0, netWorth=0, декларировать ухудшение не с чем
        for (var i = 0; i < 4; i++)
        {
            team.Debit(100m);
            bot.UpdateFinancialTrend(session);
        }

        // BuildOutSectorChain пытается достроить цепочку, но в бедственном положении (throttle=0)
        // сама достройка должна быть пустой.
        bot.BuildOutSectorChain(session);
        Assert.Empty(team.Factories);

        // Тренд разворачивается — как только throttle отрастёт, достройка должна сработать без
        // дополнительных действий (идемпотентно, тот же вызов).
        for (var i = 0; i < 4; i++)
        {
            team.Credit(1000m);
            bot.UpdateFinancialTrend(session);
        }
        bot.BuildNewlyUnlockedFactories(session);

        Assert.NotEmpty(team.Factories);
    }
}
