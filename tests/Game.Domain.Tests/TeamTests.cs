namespace Game.Domain.Tests;

public class TeamTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");
    private static readonly Sector SectorB = new("B", "Нефтегазохимия");
    private static readonly Material Ore = new("ore", "Железная руда", SectorA, level: 0);
    private static readonly Material Sheet = new("sheet", "Стальные листы", SectorA, level: 1);
    private static readonly Material Plastic = new("plastic", "Пластик", SectorB, level: 1);

    private static readonly Recipe SheetRecipe =
        new("sheet-from-ore", Sheet, 1m, new[] { new RecipeInput(Ore, 2m) }, 1m);
    private static readonly Recipe PlasticRecipe =
        new("plastic-from-ore", Plastic, 1m, new[] { new RecipeInput(Ore, 1m) }, 1m);

    private static readonly FactoryDefinition SteelMill =
        new("steel-mill", "Сталелитейный завод", SectorA, new[] { SheetRecipe });
    private static readonly FactoryDefinition PlasticPlant =
        new("plastic-plant", "Нефтехимический завод", SectorB, new[] { PlasticRecipe });

    [Fact]
    public void Construction_Throws_When_Id_Is_Empty()
    {
        Assert.Throws<ArgumentException>(() => new Team(Ulid.Empty, "Команда А1", SectorA));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Construction_Throws_When_Name_Is_Empty(string name)
    {
        Assert.Throws<ArgumentException>(() => new Team(Ulid.NewUlid(), name, SectorA));
    }

    [Fact]
    public void Construction_Starts_With_No_Factories_And_Empty_Warehouse()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);

        Assert.Empty(team.Factories);
        Assert.Empty(team.Warehouse.Stock);
        Assert.Equal(0m, team.Balance);
        Assert.Equal(0m, team.Debt);
        Assert.Equal(0m, team.PenaltyRateSurcharge);
    }

    [Fact]
    public void Credit_Increases_Balance_Only()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);

        team.Credit(100m);

        Assert.Equal(100m, team.Balance);
        Assert.Equal(0m, team.Debt);
    }

    [Fact]
    public void Debit_Decreases_Balance_And_May_Go_Negative()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);
        team.Credit(50m);

        team.Debit(70m);

        Assert.Equal(-20m, team.Balance);
    }

    [Fact]
    public void TakeLoan_Increases_Both_Balance_And_Debt_By_The_Same_Amount()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);

        team.TakeLoan(300m);

        Assert.Equal(300m, team.Balance);
        Assert.Equal(300m, team.Debt);
    }

    [Fact]
    public void RepayLoan_Decreases_Debt_Only_Balance_Is_Untouched()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);
        team.TakeLoan(300m);

        team.RepayLoan(120m);

        Assert.Equal(180m, team.Debt);
        Assert.Equal(300m, team.Balance); // погашение само по себе баланс не трогает — списывает вызывающее событие
    }

    [Fact]
    public void RepayLoan_Throws_When_Amount_Exceeds_Current_Debt()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);
        team.TakeLoan(100m);

        Assert.Throws<InvalidOperationException>(() => team.RepayLoan(100.01m));
    }

    [Fact]
    public void RepayLoan_Can_Fully_Close_The_Debt()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);
        team.TakeLoan(100m);

        team.RepayLoan(100m);

        Assert.Equal(0m, team.Debt);
    }

    [Fact]
    public void IncreasePenaltyRateSurcharge_Accumulates()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);

        team.IncreasePenaltyRateSurcharge(0.05m);
        team.IncreasePenaltyRateSurcharge(0.05m);

        Assert.Equal(0.1m, team.PenaltyRateSurcharge);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Credit_Debit_TakeLoan_And_Surcharge_Reject_Non_Positive_Amounts(decimal amount)
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);

        Assert.Throws<ArgumentOutOfRangeException>(() => team.Credit(amount));
        Assert.Throws<ArgumentOutOfRangeException>(() => team.Debit(amount));
        Assert.Throws<ArgumentOutOfRangeException>(() => team.TakeLoan(amount));
        Assert.Throws<ArgumentOutOfRangeException>(() => team.RepayLoan(amount));
        Assert.Throws<ArgumentOutOfRangeException>(() => team.IncreasePenaltyRateSurcharge(amount));
    }

    [Fact]
    public void BuildFactory_Adds_Factory_Of_Own_Sector()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);

        var factory = team.BuildFactory(Ulid.NewUlid(), SteelMill);

        Assert.Same(factory, Assert.Single(team.Factories));
        Assert.Equal(SteelMill, factory.Definition);
    }

    [Fact]
    public void BuildFactory_Throws_When_Definition_Sector_Differs_From_Team_Sector()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);

        Assert.Throws<ArgumentException>(() => team.BuildFactory(Ulid.NewUlid(), PlasticPlant));
        Assert.Empty(team.Factories);
    }
}
