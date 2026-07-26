using Game.Config.Contracts;
using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>Публичная репутация по журналу поставок/срывов/расторжений (Блок 6.2, SPEC §7).</summary>
public class ReputationCalculatorTests
{
    // HalfLifeTurns=10, WarmupTurns=3, TerminationSeverityMultiplier=3.
    private static readonly ReputationConfig Config = TestGameConfig.Resolved.Raw.Reputation;

    private static Ulid SignAndConfirmContract(EventLog<GameSessionState> log, Ulid buyerId, Ulid sellerId)
    {
        var terms = new ContractTerms(
            ContractType.Spot, TestGameConfig.Sheet, volume: 10m, unitPrice: 20m, penaltyRate: 0.1m,
            effectiveTurn: 1, spotDeliveryTurn: 1, recurringEndTurn: null);
        var contract = new Contract(Ulid.NewUlid(), buyerId, sellerId, terms, confirmationCode: "TEST01");

        log.Append(new ContractSigned { Id = Ulid.NewUlid(), Contract = ContractSpec.From(contract) });
        log.Append(new ContractConfirmed { Id = Ulid.NewUlid(), ContractId = contract.Id });

        return contract.Id;
    }

    [Fact]
    public void With_No_History_Reputation_Defaults_To_Full_Trust_With_No_Samples()
    {
        var (log, _, seller) = TestGameConfig.StartSessionWithTwoTeams();

        var result = ReputationCalculator.Calculate(log.Entries, log.State.Contracts, seller.Id, currentTurn: 1, Config);

        Assert.Equal(100m, result.Percentage);
        Assert.Equal(0, result.SampleCount);
    }

    [Fact]
    public void A_Delivery_Miss_After_Warmup_Drops_The_Percentage_To_Zero()
    {
        var (log, buyer, seller) = TestGameConfig.StartSessionWithTwoTeams();
        var contractId = SignAndConfirmContract(log, buyer.Id, seller.Id);
        log.Append(new DeliveryMissed { Id = Ulid.NewUlid(), ContractId = contractId, Turn = 5, ShortfallVolume = 10m, PenaltyAmount = 20m });

        var result = ReputationCalculator.Calculate(log.Entries, log.State.Contracts, seller.Id, currentTurn: 5, Config);

        Assert.Equal(0m, result.Percentage);
        Assert.Equal(1, result.SampleCount);
    }

    [Fact]
    public void A_Delivery_Miss_During_Warmup_Is_Forgiven_Entirely()
    {
        var (log, buyer, seller) = TestGameConfig.StartSessionWithTwoTeams();
        var contractId = SignAndConfirmContract(log, buyer.Id, seller.Id);
        log.Append(new DeliveryMissed { Id = Ulid.NewUlid(), ContractId = contractId, Turn = 2, ShortfallVolume = 10m, PenaltyAmount = 20m }); // WarmupTurns=3

        var result = ReputationCalculator.Calculate(log.Entries, log.State.Contracts, seller.Id, currentTurn: 2, Config);

        Assert.Equal(100m, result.Percentage);
        Assert.Equal(0, result.SampleCount); // не просто прощён, а исключён из выборки целиком
    }

    [Fact]
    public void A_Fresh_Miss_Weighs_More_Than_An_Older_Miss_Of_The_Same_Kind()
    {
        var (recentLog, recentBuyer, recentSeller) = TestGameConfig.StartSessionWithTwoTeams();
        var recentContract = SignAndConfirmContract(recentLog, recentBuyer.Id, recentSeller.Id);
        recentSeller.Warehouse.Add(TestGameConfig.Sheet, 10m);
        recentLog.Append(new ContractDelivered { Id = Ulid.NewUlid(), ContractId = recentContract, Turn = 10 });
        var recentMiss = SignAndConfirmContract(recentLog, recentBuyer.Id, recentSeller.Id);
        recentLog.Append(new DeliveryMissed { Id = Ulid.NewUlid(), ContractId = recentMiss, Turn = 19, ShortfallVolume = 10m, PenaltyAmount = 20m });

        var (oldLog, oldBuyer, oldSeller) = TestGameConfig.StartSessionWithTwoTeams();
        var oldContract = SignAndConfirmContract(oldLog, oldBuyer.Id, oldSeller.Id);
        oldSeller.Warehouse.Add(TestGameConfig.Sheet, 10m);
        oldLog.Append(new ContractDelivered { Id = Ulid.NewUlid(), ContractId = oldContract, Turn = 10 });
        var oldMiss = SignAndConfirmContract(oldLog, oldBuyer.Id, oldSeller.Id);
        oldLog.Append(new DeliveryMissed { Id = Ulid.NewUlid(), ContractId = oldMiss, Turn = 11, ShortfallVolume = 10m, PenaltyAmount = 20m });

        // Одинаковый успех (ход 10) в обоих сценариях; срыв тот же по тяжести, но в одном случае
        // почти свежий (ход 19), в другом — давний (ход 11) — оба смотрим с хода 20.
        var recentImpact = ReputationCalculator.Calculate(recentLog.Entries, recentLog.State.Contracts, recentSeller.Id, currentTurn: 20, Config);
        var oldImpact = ReputationCalculator.Calculate(oldLog.Entries, oldLog.State.Contracts, oldSeller.Id, currentTurn: 20, Config);

        Assert.True(recentImpact.Percentage < oldImpact.Percentage);
    }

    [Fact]
    public void Reputation_Recovers_As_A_Past_Miss_Ages_And_Successes_Accumulate()
    {
        var (log, buyer, seller) = TestGameConfig.StartSessionWithTwoTeams();
        var missContract = SignAndConfirmContract(log, buyer.Id, seller.Id);
        log.Append(new DeliveryMissed { Id = Ulid.NewUlid(), ContractId = missContract, Turn = 5, ShortfallVolume = 10m, PenaltyAmount = 20m });
        var rightAfter = ReputationCalculator.Calculate(log.Entries, log.State.Contracts, seller.Id, currentTurn: 5, Config);

        for (var turn = 6; turn <= 15; turn++)
        {
            var contractId = SignAndConfirmContract(log, buyer.Id, seller.Id);
            seller.Warehouse.Add(TestGameConfig.Sheet, 10m);
            log.Append(new ContractDelivered { Id = Ulid.NewUlid(), ContractId = contractId, Turn = turn });
        }
        var afterRecovering = ReputationCalculator.Calculate(log.Entries, log.State.Contracts, seller.Id, currentTurn: 25, Config);

        Assert.True(afterRecovering.Percentage > rightAfter.Percentage);
    }

    [Fact]
    public void A_Voluntary_Termination_Counts_Only_Against_The_Initiating_Team()
    {
        var (log, buyer, seller) = TestGameConfig.StartSessionWithTwoTeams();
        var contractId = SignAndConfirmContract(log, buyer.Id, seller.Id);
        log.Append(new ContractTerminated
        {
            Id = Ulid.NewUlid(), ContractId = contractId, Turn = 5,
            Reason = ContractTerminationReason.Voluntary, TerminatingTeamId = buyer.Id, Fee = 100m,
        });

        var buyerReputation = ReputationCalculator.Calculate(log.Entries, log.State.Contracts, buyer.Id, currentTurn: 5, Config);
        var sellerReputation = ReputationCalculator.Calculate(log.Entries, log.State.Contracts, seller.Id, currentTurn: 5, Config);

        Assert.Equal(0m, buyerReputation.Percentage);
        Assert.Equal(1, buyerReputation.SampleCount);
        Assert.Equal(100m, sellerReputation.Percentage); // не инициатор — ни при чём
        Assert.Equal(0, sellerReputation.SampleCount);
    }

    [Fact]
    public void A_Mutual_Termination_Does_Not_Affect_Anyones_Reputation()
    {
        var (log, buyer, seller) = TestGameConfig.StartSessionWithTwoTeams();
        var contractId = SignAndConfirmContract(log, buyer.Id, seller.Id);
        log.Append(new ContractTerminated
        {
            Id = Ulid.NewUlid(), ContractId = contractId, Turn = 5,
            Reason = ContractTerminationReason.Mutual, TerminatingTeamId = null, Fee = 0m,
        });

        var buyerReputation = ReputationCalculator.Calculate(log.Entries, log.State.Contracts, buyer.Id, currentTurn: 5, Config);
        var sellerReputation = ReputationCalculator.Calculate(log.Entries, log.State.Contracts, seller.Id, currentTurn: 5, Config);

        Assert.Equal(100m, buyerReputation.Percentage);
        Assert.Equal(0, buyerReputation.SampleCount);
        Assert.Equal(100m, sellerReputation.Percentage);
        Assert.Equal(0, sellerReputation.SampleCount);
    }

    [Fact]
    public void A_Voluntary_Termination_Drags_Reputation_Down_More_Than_An_Ordinary_Miss_Of_The_Same_Age()
    {
        var (missLog, missBuyer, missSeller) = TestGameConfig.StartSessionWithTwoTeams();
        var deliveredContract = SignAndConfirmContract(missLog, missBuyer.Id, missSeller.Id);
        missSeller.Warehouse.Add(TestGameConfig.Sheet, 10m);
        missLog.Append(new ContractDelivered { Id = Ulid.NewUlid(), ContractId = deliveredContract, Turn = 5 });
        var missedContract = SignAndConfirmContract(missLog, missBuyer.Id, missSeller.Id);
        missLog.Append(new DeliveryMissed { Id = Ulid.NewUlid(), ContractId = missedContract, Turn = 5, ShortfallVolume = 10m, PenaltyAmount = 20m });

        var (termLog, termBuyer, termSeller) = TestGameConfig.StartSessionWithTwoTeams();
        var deliveredContract2 = SignAndConfirmContract(termLog, termBuyer.Id, termSeller.Id);
        termSeller.Warehouse.Add(TestGameConfig.Sheet, 10m);
        termLog.Append(new ContractDelivered { Id = Ulid.NewUlid(), ContractId = deliveredContract2, Turn = 5 });
        var terminatedContract = SignAndConfirmContract(termLog, termBuyer.Id, termSeller.Id);
        termLog.Append(new ContractTerminated
        {
            Id = Ulid.NewUlid(), ContractId = terminatedContract, Turn = 5,
            Reason = ContractTerminationReason.Voluntary, TerminatingTeamId = termSeller.Id, Fee = 100m,
        });

        var missImpact = ReputationCalculator.Calculate(missLog.Entries, missLog.State.Contracts, missSeller.Id, currentTurn: 5, Config);
        var terminationImpact = ReputationCalculator.Calculate(termLog.Entries, termLog.State.Contracts, termSeller.Id, currentTurn: 5, Config);

        // Один и тот же компенсирующий успех в обоих сценариях (50% при обычном срыве); при
        // расторжении та же тяжесть удара утроена (TerminationSeverityMultiplier=3) -> 25%.
        Assert.Equal(50m, missImpact.Percentage);
        Assert.Equal(25m, terminationImpact.Percentage);
    }
}
