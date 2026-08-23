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
        // (шаг 0.25) дошёл до пола (см. SimpleBot.MinThrottle, 0.25 — уже не 0, ловушка необратимой
        // заморозки, найденная 2026-08-23) — 7 подряд деклайнов итого.
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

        // throttle упёрся в пол 0.25 -> доля = leverage(1) × throttle(0.25) = 0.25, не 0 — команда
        // притормозила, но не заморожена насмерть.
        Assert.Equal(config.Raw.GenerationResearch.MaxCommitmentPerTurn * 0.25m, team.GenerationResearchCommitmentPerTurn);
        Assert.All(team.Factories, factory => Assert.Equal(config.Raw.Rnd.MaxCommitmentPerTurn * 0.25m, factory.RndCommitmentPerTurn));
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

    /// <summary>
    /// Раньше (до 2026-08-23) финансовое бедствие полностью запрещало любую новую постройку
    /// (<c>throttle=0</c> → жёсткий бинарный бан) — необратимая ловушка на реальной цепочке
    /// (rebalance/2-sector-stepwise, 3-секторная ступенчатая, `sector2`: небольшой, но непрерывный
    /// минус не давал тренду выправиться, а без новых фабрик неоткуда было взяться доходу, который
    /// тренд бы выправил). Теперь бедствие тормозит только темп вложений в R&amp;D/поколение (см.
    /// <see cref="UpdateInvestmentPace_Reduces_The_Ceiling_Fraction_Once_Net_Worth_Keeps_Declining"/>)
    /// — постройка ограничена по-прежнему, но только честной, не завязанной на тренд толерантностью к
    /// минусу (<see cref="Game.Config.Session.StartingConditionsConfig.MaxInitialBuildBudget"/>,
    /// проверено отдельно в <c>SimpleBotStrategyTests</c>), не двойной заморозкой сверху.
    /// </summary>
    [Fact]
    public void BuildNewlyUnlockedFactories_Still_Builds_Within_Budget_Tolerance_While_In_Financial_Distress()
    {
        var config = PilotBotSession.LoadConfig();
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config, "short", endTurn: 15, new[] { new TeamSpec { Id = teamId, Name = "Бот", SectorId = sectorA.Id } });
        session.AdvancePhase(PhaseTransitionTrigger.Timer);

        // leverage=0 -> DistressThresholdTurns=1, throttle доходит до пола (SimpleBot.MinThrottle) за
        // 4 хода подряд ухудшения — самый быстрый случай.
        var bot = new SimpleBot(teamId, sectorA, config, leverage: 0m, profile: 0m);
        var team = session.State.Teams[teamId];

        bot.UpdateFinancialTrend(session); // ход 1: Balance=Debt=0, netWorth=0, декларировать ухудшение не с чем
        for (var i = 0; i < 4; i++)
        {
            team.Debit(100m);
            bot.UpdateFinancialTrend(session);
        }

        // Небольшой минус (−400) не выходит за рамки обычной, leverage-независимой от throttle
        // толерантности — постройка проходит, несмотря на бедственный тренд.
        bot.BuildOutSectorChain(session);
        Assert.NotEmpty(team.Factories);

        // При этом R&D-темп всё равно придавлен трендом (та же команда, тот же ход) — бедствие не
        // прошло бесследно, просто перестало быть двоичным запретом именно на постройку.
        bot.UpdateInvestmentPace(session);
        Assert.All(team.Factories, factory => Assert.Equal(0m, factory.RndCommitmentPerTurn));
    }
}
