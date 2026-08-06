using Game.Config.Session;

namespace Game.Engine.Tests;

/// <summary>
/// Сквозная проверка капитальных и переменных затрат фабрики через полный тик (Блок 9.3): фиксированная
/// часть — <see cref="FactoryUpkeepPaid"/> (см. также <see cref="FactoryUpkeepPaidTests"/> для
/// покрытия самого <see cref="TickFinanceStep"/>), переменная — <see cref="FactoryProduced.OverheadCost"/>
/// (энергия, зависит от объёма выпуска этого хода).
/// </summary>
public class GameSessionFactoryUpkeepTests
{
    [Fact]
    public void RunTick_Charges_Both_Fixed_Upkeep_And_Output_Proportional_Overhead()
    {
        var config = TestGameConfig.BuildWithFactoryUpkeep(fixedCostPerTurn: 10m, electricityConsumptionPerOutputUnit: 1m);
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config,
            "test",
            endTurn: 999,
            new[]
            {
                new TeamSpec { Id = teamId, Name = "Команда А1", SectorId = TestGameConfig.SectorA.Id },
            });
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision
        session.TakeLoan(teamId, 1000m);
        var factory = session.State.Teams[teamId].BuildFactory(
            Ulid.NewUlid(), config.FactoryDefinitions.Single(f => f.Id == "iron-mine"));
        factory.Hire(1);
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2

        var appended = session.RunTick(new Random(1));

        var upkeep = Assert.IsType<FactoryUpkeepPaid>(appended.Single(e => e.Change is FactoryUpkeepPaid).Change);
        Assert.Equal(10m, upkeep.Amount);

        var produced = Assert.IsType<FactoryProduced>(appended.Single(e => e.Change is FactoryProduced).Change);
        Assert.True(produced.OutputQuantity > 0); // рудник без сырьевых входов производит сразу
        // Переменная часть = объём выпуска * 1 (ставка) * ElectricityPrice хода (ElectricityBasePrice=1 в TestGameConfig, тренда нет).
        Assert.Equal(produced.OutputQuantity * 1m, produced.OverheadCost);
        Assert.True(produced.OverheadCost > 0);
    }
}
