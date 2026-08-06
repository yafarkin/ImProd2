using Game.Config.Economy;

namespace Game.Engine.Tests;

/// <summary>«Давление» недавних экстренных закупок команды по материалу (Блок 9.2) — затухающая по свежести сумма прошлых закупок, тем же приёмом полураспада, что <see cref="ReputationCalculator"/>.</summary>
public class EmergencyPurchasePressureCalculatorTests
{
    private static readonly EconomyConfig Config = TestGameConfig.Resolved.Raw.Economy with
    {
        EmergencyPurchasePressureHalfLifeTurns = 2,
    };

    private static EventLogEntry<GameSessionState> Entry(int sequenceNumber, Change<GameSessionState> change) => new()
    {
        SequenceNumber = sequenceNumber,
        Change = change,
        Timestamp = DateTimeOffset.UnixEpoch,
        PreviousHash = "genesis",
        Hash = $"hash-{sequenceNumber}",
    };

    [Fact]
    public void CalculateRecentVolume_Is_Zero_With_No_Prior_Purchases()
    {
        var teamId = Ulid.NewUlid();

        var volume = EmergencyPurchasePressureCalculator.CalculateRecentVolume(
            Array.Empty<EventLogEntry<GameSessionState>>(), teamId, "ore", currentTurn: 1, Config);

        Assert.Equal(0m, volume);
    }

    [Fact]
    public void CalculateRecentVolume_Counts_A_Purchase_Made_This_Same_Turn_At_Full_Weight()
    {
        var teamId = Ulid.NewUlid();
        var entries = new[]
        {
            Entry(0, new EmergencyPurchased { Id = Ulid.NewUlid(), TeamId = teamId, MaterialId = "ore", Volume = 10m, UnitPrice = 1m, TotalCost = 10m, Turn = 3 }),
        };

        var volume = EmergencyPurchasePressureCalculator.CalculateRecentVolume(entries, teamId, "ore", currentTurn: 3, Config);

        Assert.Equal(10m, volume);
    }

    [Fact]
    public void CalculateRecentVolume_Decays_By_Half_After_One_Half_Life()
    {
        var teamId = Ulid.NewUlid();
        var entries = new[]
        {
            Entry(0, new EmergencyPurchased { Id = Ulid.NewUlid(), TeamId = teamId, MaterialId = "ore", Volume = 10m, UnitPrice = 1m, TotalCost = 10m, Turn = 1 }),
        };

        // HalfLifeTurns=2, age = 3 - 1 = 2 -> ровно один период полураспада.
        var volume = EmergencyPurchasePressureCalculator.CalculateRecentVolume(entries, teamId, "ore", currentTurn: 3, Config);

        Assert.Equal(5m, volume);
    }

    [Fact]
    public void CalculateRecentVolume_Ignores_Purchases_By_Other_Teams()
    {
        var teamId = Ulid.NewUlid();
        var otherTeamId = Ulid.NewUlid();
        var entries = new[]
        {
            Entry(0, new EmergencyPurchased { Id = Ulid.NewUlid(), TeamId = otherTeamId, MaterialId = "ore", Volume = 10m, UnitPrice = 1m, TotalCost = 10m, Turn = 1 }),
        };

        var volume = EmergencyPurchasePressureCalculator.CalculateRecentVolume(entries, teamId, "ore", currentTurn: 1, Config);

        Assert.Equal(0m, volume);
    }

    [Fact]
    public void CalculateRecentVolume_Ignores_Purchases_Of_Other_Materials()
    {
        var teamId = Ulid.NewUlid();
        var entries = new[]
        {
            Entry(0, new EmergencyPurchased { Id = Ulid.NewUlid(), TeamId = teamId, MaterialId = "sheet", Volume = 10m, UnitPrice = 1m, TotalCost = 10m, Turn = 1 }),
        };

        var volume = EmergencyPurchasePressureCalculator.CalculateRecentVolume(entries, teamId, "ore", currentTurn: 1, Config);

        Assert.Equal(0m, volume);
    }

    [Fact]
    public void CalculateRecentVolume_Sums_Several_Purchases_With_Independent_Decay()
    {
        var teamId = Ulid.NewUlid();
        var entries = new[]
        {
            Entry(0, new EmergencyPurchased { Id = Ulid.NewUlid(), TeamId = teamId, MaterialId = "ore", Volume = 10m, UnitPrice = 1m, TotalCost = 10m, Turn = 1 }), // age 2 -> *0.5
            Entry(1, new EmergencyPurchased { Id = Ulid.NewUlid(), TeamId = teamId, MaterialId = "ore", Volume = 8m, UnitPrice = 1m, TotalCost = 8m, Turn = 3 }), // age 0 -> *1
        };

        var volume = EmergencyPurchasePressureCalculator.CalculateRecentVolume(entries, teamId, "ore", currentTurn: 3, Config);

        Assert.Equal(5m + 8m, volume);
    }
}
