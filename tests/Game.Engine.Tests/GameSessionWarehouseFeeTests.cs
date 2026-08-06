using Game.Config.Economy;
using Game.Config.Session;

namespace Game.Engine.Tests;

/// <summary>Сквозная проверка платы за превышение склада через полный тик (Блок 9.2, SPEC §5.7).</summary>
public class GameSessionWarehouseFeeTests
{
    [Fact]
    public void RunTick_Charges_A_Warehouse_Fee_When_Stock_Exceeds_The_Configured_Capacity()
    {
        var config = TestGameConfig.BuildWithWarehouse(new WarehouseConfig { FreeCapacity = 5m, OverageFeePerUnit = 1m });
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
        session.TakeLoan(teamId, 1000m); // команда сама берёт первый кредит (SPEC §5.1) — не предустановка
        session.EmergencyPurchase(teamId, "ore", 10m); // склад: 10 единиц, бесплатный лимит — 5;
        // цена = 10 (BasePrice) * 2 (EmergencyPurchaseBaseMultiplier, давления ещё нет) = 20/ед., итого 200
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        var balanceBeforeTick = session.State.Teams[teamId].Balance; // 1000 - 200 = 800

        var appended = session.RunTick(new Random(1));

        var fee = Assert.IsType<WarehouseFeeCharged>(appended.Single(e => e.Change is WarehouseFeeCharged).Change);
        Assert.Equal(5m, fee.OverageQuantity);
        Assert.Equal(5m, fee.Amount); // 5 * OverageFeePerUnit (1)
        var interest = Assert.IsType<LoanInterestCharged>(appended.Single(e => e.Change is LoanInterestCharged).Change);
        Assert.Equal(50m, interest.Amount); // 1000 * BaseLoanInterestRate (0.05), репутация 100% без истории
        Assert.Equal(balanceBeforeTick - interest.Amount - fee.Amount, session.State.Teams[teamId].Balance);
    }
}
