namespace Game.Engine.Tests;

/// <summary>
/// Разрешение заявок на аварийную закупку на расчёте (см. doc-comment <see
/// cref="EmergencyPurchaseStep"/>) — юниты самого шага; сборка в полный тик через <see
/// cref="GameSession"/> — в <see cref="GameSessionMarketTests"/>.
/// </summary>
public class EmergencyPurchaseStepTests
{
    [Fact]
    public void Run_Returns_Nothing_When_There_Are_No_Pending_Requests()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();

        var changes = EmergencyPurchaseStep.Run(team, log.State.Market, TestGameConfig.Resolved.Raw.Economy, log.Entries, currentTurn: 1);

        Assert.Empty(changes);
    }

    [Fact]
    public void Run_Emits_EmergencyPurchased_For_A_Pending_Request_At_The_Base_Multiplier()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        team.RequestEmergencyPurchase("ore", 5m);

        var changes = EmergencyPurchaseStep.Run(team, log.State.Market, TestGameConfig.Resolved.Raw.Economy, log.Entries, currentTurn: 1);

        var purchased = Assert.IsType<EmergencyPurchased>(Assert.Single(changes));
        Assert.Equal("ore", purchased.MaterialId);
        Assert.Equal(5m, purchased.Volume);
        // TestGameConfig: ore BasePrice=10, EmergencyPurchaseBaseMultiplier=2 -> 20/ед., без давления.
        Assert.Equal(20m, purchased.UnitPrice);
        Assert.Equal(100m, purchased.TotalCost);
    }

    [Fact]
    public void Run_Resolves_Several_Materials_In_A_Deterministic_Order_By_Material_Id()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        team.RequestEmergencyPurchase("sheet", 1m);
        team.RequestEmergencyPurchase("ore", 1m);

        var changes = EmergencyPurchaseStep.Run(team, log.State.Market, TestGameConfig.Resolved.Raw.Economy, log.Entries, currentTurn: 1);

        Assert.Collection(
            changes,
            change => Assert.Equal("ore", Assert.IsType<EmergencyPurchased>(change).MaterialId),
            change => Assert.Equal("sheet", Assert.IsType<EmergencyPurchased>(change).MaterialId));
    }
}
