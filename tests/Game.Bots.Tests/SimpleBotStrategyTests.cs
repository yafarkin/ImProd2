using Game.Domain;
using Game.Engine;

namespace Game.Bots.Tests;

/// <summary>
/// Две независимые оси стратегии (Блок 7.3.2, <c>docs/balancing-bots.md</c> §2): <c>leverage</c>
/// (аппетит к риску/кредиту) и <c>profile</c> (распределение усилий по времени). Регрессионный
/// ориентир — <c>leverage=1, profile=0</c> (значения по умолчанию) уже покрыт существующими тестами
/// <see cref="BotSessionRunnerTests"/>/<see cref="CrossSectorTradingTests"/>, не дублируется здесь.
/// </summary>
public class SimpleBotStrategyTests
{
    [Theory]
    [InlineData(0.0, 0.25)]
    [InlineData(1.0, 1.0)]
    [InlineData(0.5, 0.625)]
    public void BuildOutSectorChain_Scales_The_Starting_Loan_By_Leverage(double leverage, double expectedFraction)
    {
        var config = PilotBotSession.LoadConfig();
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var maxLoan = config.Raw.StartingConditions.MaxStartingLoanAmount;

        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config, "short", endTurn: 15, new[] { new TeamSpec { Id = teamId, Name = "Бот", SectorId = sectorA.Id } });
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement(1) -> Decision(1)

        var bot = new SimpleBot(teamId, sectorA, config, leverage: (decimal)leverage);
        bot.BuildOutSectorChain(session);

        Assert.Equal(maxLoan * (decimal)expectedFraction, session.State.Teams[teamId].PendingLoanTakeAmount);
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

    [Theory]
    [InlineData(1.0, false)] // leverage=1 — не спешит с добровольным погашением (доля 0).
    [InlineData(0.0, true)] // leverage=0 — гасит долг при первой возможности (доля 1).
    public void RepayDebt_Gates_The_Voluntary_Repayment_Share_By_Leverage(double leverage, bool expectRepayment)
    {
        var config = PilotBotSession.LoadConfig();
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config, "short", endTurn: 15, new[] { new TeamSpec { Id = teamId, Name = "Бот", SectorId = sectorA.Id } });
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement(1) -> Decision(1)

        // Нарочно без BuildOutSectorChain/фабрик — изолирует поведение RepayDebt от стоимости
        // построения цепочки: заём материализуется в Balance/Debt, буфер на ближайший ход у команды
        // без фабрик и рабочих — это только обязательный платёж и проценты, заведомо меньше самого
        // займа, так что свободный остаток предсказуемо положительный при любом leverage.
        session.TakeLoan(teamId, 50_000m);
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision(1) -> Settlement(2)
        session.RunTick(new Random(1)); // материализует заём
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement(2) -> Decision(2)

        var team = session.State.Teams[teamId];
        Assert.True(team.Debt > 0m);
        Assert.True(team.Balance > 0m);

        var bot = new SimpleBot(teamId, sectorA, config, leverage: (decimal)leverage);
        bot.RepayDebt(session);

        if (expectRepayment)
        {
            Assert.True(team.PendingLoanRepayAmount > 0m);
        }
        else
        {
            Assert.Equal(0m, team.PendingLoanRepayAmount);
        }
    }
}
