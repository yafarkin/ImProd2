namespace Game.Engine.Tests;

public class GenerationResearchStepTests
{
    // Пороги в очках исследований: 100^0.5=10, 400^0.5=20 — накопленные ¤ 100 и 400 (1->2, 2->3).
    private static readonly Config.Economy.GenerationResearchConfig Config = new()
    {
        StartingGeneration = 1,
        ResearchPointThresholdsByGeneration = new[] { 10m, 20m },
        DiminishingReturnsExponent = 0.5m,
        MaxCommitmentPerTurn = 300m,
    };

    [Fact]
    public void Run_Returns_Only_The_Investment_When_The_Threshold_Is_Not_Reached()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();

        var changes = GenerationResearchStep.Run(team.Id, team, 50m, Config);

        var invested = Assert.Single(changes);
        Assert.IsType<GenerationResearchInvested>(invested);
    }

    [Fact]
    public void Run_Appends_A_Generation_Advance_When_The_Investment_Reaches_The_Threshold()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();

        var changes = GenerationResearchStep.Run(team.Id, team, 100m, Config);

        Assert.Equal(2, changes.Count);
        Assert.IsType<GenerationResearchInvested>(changes[0]);
        var generationAdvanced = Assert.IsType<TeamGenerationAdvanced>(changes[1]);
        Assert.Equal(2, generationAdvanced.NewGeneration);
    }

    [Fact]
    public void Applying_The_Returned_Changes_End_To_End_Updates_Balance_Investment_And_Generation()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        team.Credit(1000m);

        foreach (var change in GenerationResearchStep.Run(team.Id, team, 100m, Config))
        {
            log.Append(change);
        }

        Assert.Equal(900m, team.Balance);
        Assert.Equal(100m, team.GenerationResearchInvestment);
        Assert.Equal(2, team.UnlockedGeneration);
        Assert.True(log.VerifyIntegrity());
    }

    [Fact]
    public void Run_Does_Nothing_And_Charges_Nothing_Once_The_Team_Is_Already_At_The_Max_Generation()
    {
        // Баг-репорт пользователя: раньше деньги продолжали списываться каждый ход даже после того,
        // как команда уже разблокировала максимальное поколение — вкладывать дальше было некуда.
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        team.Credit(1000m);
        foreach (var change in GenerationResearchStep.Run(team.Id, team, 400m, Config)) // оба порога разом
        {
            log.Append(change);
        }
        Assert.Equal(3, team.UnlockedGeneration); // максимальное поколение при порогах {10, 20}
        var balanceAfterMaxed = team.Balance;

        var changes = GenerationResearchStep.Run(team.Id, team, 50m, Config);

        Assert.Empty(changes);
        Assert.Equal(balanceAfterMaxed, team.Balance);
    }
}
