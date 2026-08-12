using Game.Domain;

namespace Game.Engine.Tests;

public class ContractExecutionTests
{
    // Подтверждаем на effectiveTurn-1 — Contract.ResolveTermsForActivation переносит настоящий
    // EffectiveTurn на currentTurn+1 (подтверждение только в фазе решений, расчёт currentTurn уже
    // прошёл), так что итоговые EffectiveTurn/DeliveryTurn/EndTurn контракта в точности совпадают с
    // тем, что передал вызывающий тест.
    private static Contract SpotContract(int effectiveTurn, int deliveryTurn)
    {
        var terms = new ContractTerms(
            ContractType.Spot, TestGameConfig.Sheet, 10m, 20m, 0.1m, effectiveTurn, deliveryTurn, recurringEndTurn: null);
        var contract = new Contract(Ulid.NewUlid(), Ulid.NewUlid(), Ulid.NewUlid(), terms, "ABC123");
        contract.Confirm(TeamRole.Manager, contract.BuyerTeamId, effectiveTurn - 1);
        return contract;
    }

    private static Contract RecurringContract(int effectiveTurn, int? endTurn)
    {
        var terms = new ContractTerms(
            ContractType.Recurring, TestGameConfig.Sheet, 10m, 20m, 0.1m, effectiveTurn, spotDeliveryTurn: null, recurringEndTurn: endTurn);
        var contract = new Contract(Ulid.NewUlid(), Ulid.NewUlid(), Ulid.NewUlid(), terms, "ABC123");
        contract.Confirm(TeamRole.Manager, contract.BuyerTeamId, effectiveTurn - 1);
        return contract;
    }

    [Fact]
    public void Spot_Delivery_Is_Due_Only_On_Its_Delivery_Turn()
    {
        var contract = SpotContract(effectiveTurn: 2, deliveryTurn: 4);

        Assert.False(ContractExecution.IsDeliveryDue(contract, 3));
        Assert.True(ContractExecution.IsDeliveryDue(contract, 4));
        Assert.False(ContractExecution.IsDeliveryDue(contract, 5));
    }

    [Fact]
    public void Recurring_Delivery_Is_Due_Every_Turn_In_Range_Inclusive()
    {
        var contract = RecurringContract(effectiveTurn: 2, endTurn: 4);

        Assert.False(ContractExecution.IsDeliveryDue(contract, 1));
        Assert.True(ContractExecution.IsDeliveryDue(contract, 2));
        Assert.True(ContractExecution.IsDeliveryDue(contract, 3));
        Assert.True(ContractExecution.IsDeliveryDue(contract, 4));
        Assert.False(ContractExecution.IsDeliveryDue(contract, 5));
    }

    /// <summary>Бессрочный recurring (нет RecurringEndTurn, запрос пользователя) — поставка due на каждом ходу с момента вступления в силу, без верхней границы.</summary>
    [Fact]
    public void Indefinite_Recurring_Delivery_Is_Due_On_Every_Turn_From_Effective_Turn_Onward()
    {
        var contract = RecurringContract(effectiveTurn: 2, endTurn: null);

        Assert.False(ContractExecution.IsDeliveryDue(contract, 1));
        Assert.True(ContractExecution.IsDeliveryDue(contract, 2));
        Assert.True(ContractExecution.IsDeliveryDue(contract, 3));
        Assert.True(ContractExecution.IsDeliveryDue(contract, 1000)); // сколько угодно далеко — верхней границы нет
    }

    [Fact]
    public void A_Contract_That_Is_Not_Active_Is_Never_Due()
    {
        var terms = new ContractTerms(
            ContractType.Spot, TestGameConfig.Sheet, 10m, 20m, 0.1m, effectiveTurn: 1, spotDeliveryTurn: 1, recurringEndTurn: null);
        var pending = new Contract(Ulid.NewUlid(), Ulid.NewUlid(), Ulid.NewUlid(), terms, "ABC123");

        Assert.False(ContractExecution.IsDeliveryDue(pending, 1)); // ещё не подтверждён
    }
}
