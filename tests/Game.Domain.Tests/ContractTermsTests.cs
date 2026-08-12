namespace Game.Domain.Tests;

public class ContractTermsTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");
    private static readonly Material Sheet = new("sheet", "Стальные листы", SectorA, level: 1);

    private static ContractTerms Spot(int? deliveryTurn = 5) =>
        new(ContractType.Spot, Sheet, volume: 10m, unitPrice: 20m, penaltyRate: 0.1m,
            effectiveTurn: 3, spotDeliveryTurn: deliveryTurn, recurringEndTurn: null);

    private static ContractTerms Recurring(int? endTurn = 15) =>
        new(ContractType.Recurring, Sheet, volume: 10m, unitPrice: 20m, penaltyRate: 0.1m,
            effectiveTurn: 3, spotDeliveryTurn: null, recurringEndTurn: endTurn);

    [Fact]
    public void Construction_Succeeds_For_A_Valid_Spot_Contract()
    {
        var terms = Spot();

        Assert.Equal(ContractType.Spot, terms.Type);
        Assert.Equal(5, terms.SpotDeliveryTurn);
        Assert.Null(terms.RecurringEndTurn);
    }

    [Fact]
    public void Construction_Succeeds_For_A_Valid_Recurring_Contract()
    {
        var terms = Recurring();

        Assert.Equal(ContractType.Recurring, terms.Type);
        Assert.Equal(15, terms.RecurringEndTurn);
        Assert.Null(terms.SpotDeliveryTurn);
    }

    [Fact]
    public void Construction_Throws_When_A_Spot_Contract_Has_No_Delivery_Turn()
    {
        Assert.Throws<ArgumentException>(() => Spot(deliveryTurn: null));
    }

    [Fact]
    public void Construction_Throws_When_A_Spot_Contract_Also_Specifies_A_Recurring_End_Turn()
    {
        Assert.Throws<ArgumentException>(() =>
            new ContractTerms(ContractType.Spot, Sheet, 10m, 20m, 0.1m, 3, spotDeliveryTurn: 5, recurringEndTurn: 8));
    }

    /// <summary>Отсутствие конечного хода — не ошибка, а осознанный «бессрочный» recurring (до расторжения одной из сторон), запрос пользователя.</summary>
    [Fact]
    public void Construction_Succeeds_For_An_Indefinite_Recurring_Contract_With_No_End_Turn()
    {
        var terms = Recurring(endTurn: null);

        Assert.Equal(ContractType.Recurring, terms.Type);
        Assert.Null(terms.RecurringEndTurn);
    }

    [Fact]
    public void Construction_Throws_When_A_Recurring_Contract_Has_A_Non_Positive_End_Turn()
    {
        Assert.Throws<ArgumentException>(() => Recurring(endTurn: 0));
    }

    [Fact]
    public void Construction_Throws_When_A_Recurring_Contract_Also_Specifies_A_Spot_Delivery_Turn()
    {
        Assert.Throws<ArgumentException>(() =>
            new ContractTerms(ContractType.Recurring, Sheet, 10m, 20m, 0.1m, 3, spotDeliveryTurn: 5, recurringEndTurn: 15));
    }

    [Fact]
    public void Construction_Throws_When_The_Delivery_Turn_Precedes_The_Effective_Turn()
    {
        Assert.Throws<ArgumentException>(() =>
            new ContractTerms(ContractType.Spot, Sheet, 10m, 20m, 0.1m, effectiveTurn: 5, spotDeliveryTurn: 3, recurringEndTurn: null));
    }

    [Fact]
    public void Construction_Throws_When_The_Recurring_End_Turn_Precedes_The_Effective_Turn()
    {
        Assert.Throws<ArgumentException>(() =>
            new ContractTerms(ContractType.Recurring, Sheet, 10m, 20m, 0.1m, effectiveTurn: 5, spotDeliveryTurn: null, recurringEndTurn: 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Construction_Throws_When_Volume_Is_Not_Positive(decimal volume)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ContractTerms(ContractType.Spot, Sheet, volume, 20m, 0.1m, 3, 5, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Construction_Throws_When_Unit_Price_Is_Not_Positive(decimal unitPrice)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ContractTerms(ContractType.Spot, Sheet, 10m, unitPrice, 0.1m, 3, 5, null));
    }

    [Fact]
    public void Construction_Throws_When_Penalty_Rate_Is_Negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ContractTerms(ContractType.Spot, Sheet, 10m, 20m, penaltyRate: -0.01m, 3, 5, null));
    }

    [Fact]
    public void Two_Terms_With_Identical_Values_Are_Equal()
    {
        Assert.Equal(Spot(), Spot());
    }

    [Fact]
    public void Two_Terms_Differing_In_Volume_Are_Not_Equal()
    {
        var a = Spot();
        var b = new ContractTerms(ContractType.Spot, Sheet, 11m, 20m, 0.1m, 3, 5, null);

        Assert.NotEqual(a, b);
    }
}
