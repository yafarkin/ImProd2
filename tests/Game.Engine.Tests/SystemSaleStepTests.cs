namespace Game.Engine.Tests;

/// <summary>
/// Разрешение заявок на продажу системе на расчёте (см. doc-comment <see cref="SystemSaleStep"/>) —
/// юниты самого шага; сборка в полный тик (включая порядок команд) — в <see
/// cref="GameSessionMarketTests"/>.
/// </summary>
public class SystemSaleStepTests
{
    [Fact]
    public void Run_Returns_Nothing_When_There_Are_No_Pending_Requests()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();

        var changes = SystemSaleStep.Run(team, log.State.Market, TestGameConfig.MaterialCosts, TestGameConfig.Resolved.Raw.Economy, TestGameConfig.Resolved.Materials);

        Assert.Empty(changes);
    }

    [Fact]
    public void Run_Emits_MaterialSoldToSystem_For_A_Pending_Request_Within_Capacity()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        team.Warehouse.Add(TestGameConfig.Ore, 20m, 0m);
        team.RequestSaleToSystem("ore", 20m);

        var changes = SystemSaleStep.Run(team, log.State.Market, TestGameConfig.MaterialCosts, TestGameConfig.Resolved.Raw.Economy, TestGameConfig.Resolved.Materials);

        var sold = Assert.IsType<MaterialSoldToSystem>(Assert.Single(changes));
        Assert.Equal(20m, sold.Volume);
        Assert.Equal(130m, sold.TotalRevenue); // ore: себестоимость 5, SystemSaleMarginMultiplier 1.30
    }

    [Fact]
    public void Run_Caps_A_Sale_Request_To_The_Actual_Stock()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        team.Warehouse.Add(TestGameConfig.Ore, 3m, 0m);
        team.RequestSaleToSystem("ore", 20m); // просит больше, чем реально есть

        var changes = SystemSaleStep.Run(team, log.State.Market, TestGameConfig.MaterialCosts, TestGameConfig.Resolved.Raw.Economy, TestGameConfig.Resolved.Materials);

        var sold = Assert.IsType<MaterialSoldToSystem>(Assert.Single(changes));
        Assert.Equal(3m, sold.Volume); // урезано до реального остатка
    }

    [Fact]
    public void Run_Emits_A_Zero_Sale_To_Clear_A_Stale_Request_When_There_Is_No_Stock()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam(); // склада нет вообще
        team.RequestSaleToSystem("ore", 5m);

        var changes = SystemSaleStep.Run(team, log.State.Market, TestGameConfig.MaterialCosts, TestGameConfig.Resolved.Raw.Economy, TestGameConfig.Resolved.Materials);

        var sold = Assert.IsType<MaterialSoldToSystem>(Assert.Single(changes));
        Assert.Equal(0m, sold.Volume);
    }

    [Fact]
    public void Run_Resolves_Several_Materials_In_A_Deterministic_Order_By_Material_Id()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        team.Warehouse.Add(TestGameConfig.Ore, 5m, 0m);
        team.Warehouse.Add(TestGameConfig.Sheet, 5m, 0m);
        team.RequestSaleToSystem("sheet", 1m);
        team.RequestSaleToSystem("ore", 1m);

        var changes = SystemSaleStep.Run(team, log.State.Market, TestGameConfig.MaterialCosts, TestGameConfig.Resolved.Raw.Economy, TestGameConfig.Resolved.Materials);

        Assert.Collection(
            changes,
            change => Assert.Equal("ore", Assert.IsType<MaterialSoldToSystem>(change).MaterialId),
            change => Assert.Equal("sheet", Assert.IsType<MaterialSoldToSystem>(change).MaterialId));
    }
}
