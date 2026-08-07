namespace Game.Domain.Tests;

public class FactoryTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");
    private static readonly Sector SectorB = new("B", "Нефтегазохимия");
    private static readonly Material Ore = new("ore", "Железная руда", SectorA, level: 0);
    private static readonly Material Sheet = new("sheet", "Стальные листы", SectorA, level: 1);
    private static readonly Material Rebar = new("rebar", "Арматура", SectorA, level: 2);

    // Recipe/FactoryDefinition сравниваются по ссылке (это объекты графа конфигурации, загружаемые
    // один раз за сессию), поэтому тесты переиспользуют эти экземпляры, а не создают похожие копии.
    private static readonly Recipe SheetRecipe =
        new("sheet-from-ore", Sheet, 1m, new[] { new RecipeInput(Ore, 2m) }, 1m);
    private static readonly Recipe RebarRecipe =
        new("rebar-from-sheet", Rebar, 1m, new[] { new RecipeInput(Sheet, 1m) }, 1m);

    private static readonly FactoryDefinition MultiRecipeMill =
        new("steel-mill", "Сталелитейный завод", SectorA, new[] { SheetRecipe, RebarRecipe });
    private static readonly FactoryDefinition SingleRecipeMill =
        new("basic-mill", "Базовый завод", SectorA, new[] { SheetRecipe });

    [Fact]
    public void Construction_Succeeds_And_Defaults_To_First_Recipe_And_Level_One()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        Assert.Equal(0, factory.Workers);
        Assert.Equal(1, factory.Level);
        Assert.Equal(0m, factory.RndInvestment);
        Assert.Same(SheetRecipe, factory.SelectedRecipe);
    }

    [Fact]
    public void Construction_Throws_When_Id_Is_Empty()
    {
        Assert.Throws<ArgumentException>(() => new Factory(Ulid.Empty, SectorA, MultiRecipeMill));
    }

    [Fact]
    public void Construction_Throws_When_Owner_Sector_Does_Not_Match_Definition_Sector()
    {
        Assert.Throws<ArgumentException>(() => new Factory(Ulid.NewUlid(), SectorB, MultiRecipeMill));
    }

    [Fact]
    public void Construction_Throws_When_Selected_Recipe_Not_In_Definition()
    {
        Assert.Throws<ArgumentException>(() => new Factory(Ulid.NewUlid(), SectorA, SingleRecipeMill, RebarRecipe));
    }

    [Fact]
    public void Hire_Increases_Workers()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        factory.Hire(5);

        Assert.Equal(5, factory.Workers);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Hire_Throws_When_Count_Is_Not_Positive(int count)
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        Assert.Throws<ArgumentOutOfRangeException>(() => factory.Hire(count));
    }

    [Fact]
    public void Fire_Decreases_Workers()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);
        factory.Hire(5);

        factory.Fire(2);

        Assert.Equal(3, factory.Workers);
    }

    [Fact]
    public void Fire_Throws_When_More_Than_Current_Workers()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);
        factory.Hire(2);

        Assert.Throws<InvalidOperationException>(() => factory.Fire(3));
        Assert.Equal(2, factory.Workers);
    }

    [Fact]
    public void SelectRecipe_Switches_Between_Definitions_Recipes()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        factory.SelectRecipe(RebarRecipe);

        Assert.Same(RebarRecipe, factory.SelectedRecipe);
    }

    [Fact]
    public void SelectRecipe_Throws_When_Recipe_Not_In_Definition()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);
        var foreignRecipe = new Recipe("foreign", Rebar, 1m, new[] { new RecipeInput(Sheet, 1m) }, 1m);

        Assert.Throws<ArgumentException>(() => factory.SelectRecipe(foreignRecipe));
    }

    [Fact]
    public void InvestInRnd_Accumulates()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        factory.InvestInRnd(100m);
        factory.InvestInRnd(50m);

        Assert.Equal(150m, factory.RndInvestment);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvestInRnd_Throws_When_Amount_Is_Not_Positive(decimal amount)
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        Assert.Throws<ArgumentOutOfRangeException>(() => factory.InvestInRnd(amount));
    }

    [Fact]
    public void AdvanceLevel_Increments_Level()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        factory.AdvanceLevel();

        Assert.Equal(2, factory.Level);
    }

    [Fact]
    public void Construction_Defaults_AllocationShare_To_One()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        Assert.Equal(1m, factory.AllocationShare);
    }

    [Fact]
    public void SetAllocationShare_Changes_The_Share()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        factory.SetAllocationShare(60m);

        Assert.Equal(60m, factory.AllocationShare);
    }

    [Fact]
    public void SetAllocationShare_Accepts_Zero()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        factory.SetAllocationShare(0m);

        Assert.Equal(0m, factory.AllocationShare);
    }

    [Fact]
    public void SetAllocationShare_Throws_When_Negative()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        Assert.Throws<ArgumentOutOfRangeException>(() => factory.SetAllocationShare(-1m));
    }

    [Fact]
    public void SetRndCommitment_Changes_The_Commitment()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        factory.SetRndCommitment(50m);

        Assert.Equal(50m, factory.RndCommitmentPerTurn);
    }

    [Fact]
    public void SetRndCommitment_Accepts_Zero()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);
        factory.SetRndCommitment(50m);

        factory.SetRndCommitment(0m);

        Assert.Equal(0m, factory.RndCommitmentPerTurn);
    }

    [Fact]
    public void SetRndCommitment_Throws_When_Negative()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        Assert.Throws<ArgumentOutOfRangeException>(() => factory.SetRndCommitment(-1m));
    }

    [Fact]
    public void Construction_Defaults_Condition_To_1_And_LastResetTurn_To_The_Given_BuiltAtTurn()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill, builtAtTurn: 7);

        Assert.Equal(1m, factory.Condition);
        Assert.Equal(7, factory.LastResetTurn);
        Assert.False(factory.IsUnderRepair);
        Assert.Equal(0, factory.RepairTurnsRemaining);
    }

    [Fact]
    public void SetOverhaulRequested_Changes_The_Flag()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        factory.SetOverhaulRequested(true);

        Assert.True(factory.OverhaulRequested);

        factory.SetOverhaulRequested(false);

        Assert.False(factory.OverhaulRequested);
    }

    [Fact]
    public void ApplyConditionChange_Sets_The_Condition()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        factory.ApplyConditionChange(0.7m);

        Assert.Equal(0.7m, factory.Condition);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void ApplyConditionChange_Throws_Outside_The_0_To_1_Range(decimal condition)
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        Assert.Throws<ArgumentOutOfRangeException>(() => factory.ApplyConditionChange(condition));
    }

    [Fact]
    public void StartRepair_Fixes_The_Condition_And_Captures_The_Repair_Parameters()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);
        factory.SetOverhaulRequested(true);

        factory.StartRepair(
            conditionAtEntry: 0.45m, durationTurns: 4, outputMultiplier: 0m,
            salaryRate: 0.66m, upkeepRate: 0.5m, targetCondition: 1m);

        Assert.Equal(0.45m, factory.Condition);
        Assert.True(factory.IsUnderRepair);
        Assert.Equal(4, factory.RepairTurnsRemaining);
        Assert.Equal(0m, factory.RepairOutputMultiplier);
        Assert.Equal(0.66m, factory.RepairSalaryRate);
        Assert.Equal(0.5m, factory.RepairUpkeepRate);
        Assert.Equal(1m, factory.RepairTargetCondition);
        Assert.False(factory.OverhaulRequested); // запрос удовлетворён простоем — сброшен
    }

    [Fact]
    public void AdvanceRepairTurn_Decrements_The_Remaining_Turns()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);
        factory.StartRepair(0.4m, durationTurns: 3, outputMultiplier: 0m, salaryRate: 0.66m, upkeepRate: 0.5m, targetCondition: 1m);

        factory.AdvanceRepairTurn();

        Assert.Equal(2, factory.RepairTurnsRemaining);
        Assert.True(factory.IsUnderRepair);
    }

    [Fact]
    public void AdvanceRepairTurn_Throws_When_Not_Under_Repair()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        Assert.Throws<InvalidOperationException>(() => factory.AdvanceRepairTurn());
    }

    [Fact]
    public void CompleteRepair_Returns_The_Factory_To_Service_At_The_Captured_Target_And_Resets_The_Age_Clock()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill, builtAtTurn: 1);
        factory.StartRepair(0.4m, durationTurns: 1, outputMultiplier: 0m, salaryRate: 0.66m, upkeepRate: 0.5m, targetCondition: 0.85m);

        factory.CompleteRepair(currentTurn: 20);

        Assert.Equal(0.85m, factory.Condition);
        Assert.False(factory.IsUnderRepair);
        Assert.Equal(0, factory.RepairTurnsRemaining);
        Assert.Equal(20, factory.LastResetTurn);
    }

    [Fact]
    public void CompleteRepair_Throws_When_Not_Under_Repair()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        Assert.Throws<InvalidOperationException>(() => factory.CompleteRepair(currentTurn: 5));
    }
}
