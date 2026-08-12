namespace Game.Domain.Tests;

public class ContractTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");
    private static readonly Material Sheet = new("sheet", "Стальные листы", SectorA, level: 1);

    private static readonly ContractTerms Terms =
        new(ContractType.Spot, Sheet, 10m, 20m, 0.1m, effectiveTurn: 3, spotDeliveryTurn: 5, recurringEndTurn: null);

    private static Contract NewPendingContract() =>
        new(Ulid.NewUlid(), Ulid.NewUlid(), Ulid.NewUlid(), Terms, "ABC123");

    [Fact]
    public void Construction_Starts_In_PendingConfirmation_With_No_Termination_Reason()
    {
        var contract = NewPendingContract();

        Assert.Equal(ContractStatus.PendingConfirmation, contract.Status);
        Assert.Null(contract.TerminationReason);
    }

    [Fact]
    public void Construction_Throws_When_Buyer_And_Seller_Are_The_Same_Team()
    {
        var teamId = Ulid.NewUlid();

        Assert.Throws<ArgumentException>(() => new Contract(Ulid.NewUlid(), teamId, teamId, Terms, "ABC123"));
    }

    [Fact]
    public void Confirm_By_A_Manager_Activates_The_Contract()
    {
        var contract = NewPendingContract();

        contract.Confirm(TeamRole.Manager, contract.BuyerTeamId, 5);

        Assert.Equal(ContractStatus.Active, contract.Status);
    }

    [Fact]
    public void Confirm_By_A_Negotiator_Throws_And_Leaves_The_Contract_Pending()
    {
        var contract = NewPendingContract();

        Assert.Throws<InvalidOperationException>(() => contract.Confirm(TeamRole.Negotiator, contract.BuyerTeamId, 5));

        Assert.Equal(ContractStatus.PendingConfirmation, contract.Status);
    }

    [Fact]
    public void Confirm_By_A_Team_Not_Party_To_The_Contract_Throws()
    {
        var contract = NewPendingContract();

        Assert.Throws<InvalidOperationException>(() => contract.Confirm(TeamRole.Manager, Ulid.NewUlid(), 5));

        Assert.Equal(ContractStatus.PendingConfirmation, contract.Status);
    }

    [Fact]
    public void Confirm_By_The_Proposing_Team_Throws_Only_The_Counterparty_Can_Confirm()
    {
        var buyerId = Ulid.NewUlid();
        var sellerId = Ulid.NewUlid();
        var contract = new Contract(Ulid.NewUlid(), buyerId, sellerId, Terms, "ABC123", proposedByTeamId: buyerId);

        Assert.Throws<InvalidOperationException>(() => contract.Confirm(TeamRole.Manager, buyerId, 5));

        Assert.Equal(ContractStatus.PendingConfirmation, contract.Status);
    }

    [Fact]
    public void Confirm_By_The_Counterparty_Of_The_Proposing_Team_Activates_The_Contract()
    {
        var buyerId = Ulid.NewUlid();
        var sellerId = Ulid.NewUlid();
        var contract = new Contract(Ulid.NewUlid(), buyerId, sellerId, Terms, "ABC123", proposedByTeamId: buyerId);

        contract.Confirm(TeamRole.Manager, sellerId, 5);

        Assert.Equal(ContractStatus.Active, contract.Status);
    }

    [Fact]
    public void Confirm_Throws_When_The_Contract_Is_Already_Active()
    {
        var contract = NewPendingContract();
        contract.Confirm(TeamRole.Manager, contract.BuyerTeamId, 5);

        Assert.Throws<InvalidOperationException>(() => contract.Confirm(TeamRole.Manager, contract.SellerTeamId, 5));
    }

    [Fact]
    public void ConfirmAutomatically_Activates_The_Contract_Without_Checking_Sides()
    {
        var contract = NewPendingContract();

        contract.ConfirmAutomatically();

        Assert.Equal(ContractStatus.Active, contract.Status);
    }

    [Fact]
    public void Terminate_An_Active_Contract_Records_The_Reason()
    {
        var contract = NewPendingContract();
        contract.Confirm(TeamRole.Manager, contract.BuyerTeamId, 5);

        contract.Terminate(ContractTerminationReason.Mutual);

        Assert.Equal(ContractStatus.Terminated, contract.Status);
        Assert.Equal(ContractTerminationReason.Mutual, contract.TerminationReason);
    }

    [Fact]
    public void Terminate_Throws_When_The_Contract_Is_Still_Pending_Confirmation()
    {
        var contract = NewPendingContract();

        Assert.Throws<InvalidOperationException>(() => contract.Terminate(ContractTerminationReason.Voluntary));
    }

    [Fact]
    public void Terminate_Throws_When_The_Contract_Is_Already_Terminated()
    {
        var contract = NewPendingContract();
        contract.Confirm(TeamRole.Manager, contract.BuyerTeamId, 5);
        contract.Terminate(ContractTerminationReason.Mutual);

        Assert.Throws<InvalidOperationException>(() => contract.Terminate(ContractTerminationReason.Voluntary));
    }

    [Fact]
    public void Complete_Moves_An_Active_Spot_Contract_To_Completed()
    {
        var contract = NewPendingContract(); // Terms — spot
        contract.Confirm(TeamRole.Manager, contract.BuyerTeamId, 5);

        contract.Complete();

        Assert.Equal(ContractStatus.Completed, contract.Status);
    }

    [Fact]
    public void Complete_Throws_For_A_Recurring_Contract()
    {
        var recurringTerms = new ContractTerms(
            ContractType.Recurring, Sheet, 10m, 20m, 0.1m, effectiveTurn: 3, spotDeliveryTurn: null, recurringEndTurn: 15);
        var contract = new Contract(Ulid.NewUlid(), Ulid.NewUlid(), Ulid.NewUlid(), recurringTerms, "ABC123");
        contract.Confirm(TeamRole.Manager, contract.BuyerTeamId, 5);

        Assert.Throws<InvalidOperationException>(() => contract.Complete());
    }

    [Fact]
    public void Complete_Throws_When_The_Contract_Is_Not_Active()
    {
        var contract = NewPendingContract();

        Assert.Throws<InvalidOperationException>(() => contract.Complete());
    }

    /// <summary>
    /// Живой лог: recurring-контракт заключён с EffectiveTurn=8, RecurringEndTurn=8 (окно в один
    /// ход) — управляющий контрагента подтвердил его только на ходу 10, когда исходное окно давно
    /// прошло. Раньше контракт становился Active с тем же EffectiveTurn=8, и <see
    /// cref="Game.Engine.ContractExecution.IsDeliveryDue"/> для него навсегда возвращал false — ни
    /// поставки, ни срыва, ни малейшего сигнала в интерфейсе. Теперь окно пересчитывается от
    /// ближайшего реально достижимого хода после подтверждения, сохраняя исходную длительность
    /// (тут — 1 ход).
    /// </summary>
    [Fact]
    public void Confirm_Resolves_A_Recurring_Contracts_Window_From_The_Confirmation_Turn_Not_The_Proposal_Turn()
    {
        var terms = new ContractTerms(
            ContractType.Recurring, Sheet, 10m, 20m, 0.1m, effectiveTurn: 8, spotDeliveryTurn: null, recurringEndTurn: 8);
        var contract = new Contract(Ulid.NewUlid(), Ulid.NewUlid(), Ulid.NewUlid(), terms, "ABC123");

        contract.Confirm(TeamRole.Manager, contract.BuyerTeamId, currentTurn: 10);

        Assert.Equal(11, contract.Terms.EffectiveTurn); // подтверждение — в фазе решений хода 10, расчёт хода 10 уже прошёл, ближайший достижимый — 11
        Assert.Equal(11, contract.Terms.RecurringEndTurn); // та же длительность (1 ход)
    }

    /// <summary>Длительность (не только сам факт «окно не пустое») переживает разрешение — 5 ходов, предложенных при заявке, остаются 5 ходами, отсчитанными от ближайшего достижимого хода после подтверждения.</summary>
    [Fact]
    public void Confirm_Preserves_The_Originally_Negotiated_Recurring_Duration()
    {
        var terms = new ContractTerms(
            ContractType.Recurring, Sheet, 10m, 20m, 0.1m, effectiveTurn: 1, spotDeliveryTurn: null, recurringEndTurn: 5); // 5 ходов
        var contract = new Contract(Ulid.NewUlid(), Ulid.NewUlid(), Ulid.NewUlid(), terms, "ABC123");

        contract.Confirm(TeamRole.Manager, contract.BuyerTeamId, currentTurn: 20);

        Assert.Equal(21, contract.Terms.EffectiveTurn);
        Assert.Equal(25, contract.Terms.RecurringEndTurn); // 5 ходов, начиная с 21-го: 21..25
    }

    /// <summary>Ход поставки spot-контракта, если он ещё не наступил к моменту подтверждения, остаётся как согласовали — подтверждение его не трогает.</summary>
    [Fact]
    public void Confirm_Leaves_A_Future_Spot_Delivery_Turn_Untouched()
    {
        var terms = new ContractTerms(
            ContractType.Spot, Sheet, 10m, 20m, 0.1m, effectiveTurn: 1, spotDeliveryTurn: 15, recurringEndTurn: null);
        var contract = new Contract(Ulid.NewUlid(), Ulid.NewUlid(), Ulid.NewUlid(), terms, "ABC123");

        contract.Confirm(TeamRole.Manager, contract.BuyerTeamId, currentTurn: 10);

        Assert.Equal(15, contract.Terms.SpotDeliveryTurn);
    }

    /// <summary>Симметричный случай для spot: если согласованный ход поставки уже прошёл к моменту подтверждения, поставка сдвигается на ближайший реально достижимый ход — не остаётся недостижимой навсегда.</summary>
    [Fact]
    public void Confirm_Moves_An_Already_Elapsed_Spot_Delivery_Turn_Forward_To_The_Next_Reachable_Turn()
    {
        var terms = new ContractTerms(
            ContractType.Spot, Sheet, 10m, 20m, 0.1m, effectiveTurn: 1, spotDeliveryTurn: 5, recurringEndTurn: null);
        var contract = new Contract(Ulid.NewUlid(), Ulid.NewUlid(), Ulid.NewUlid(), terms, "ABC123");

        contract.Confirm(TeamRole.Manager, contract.BuyerTeamId, currentTurn: 10);

        Assert.Equal(11, contract.Terms.SpotDeliveryTurn); // расчёт хода 10 уже прошёл — ближайший достижимый 11
    }
}
