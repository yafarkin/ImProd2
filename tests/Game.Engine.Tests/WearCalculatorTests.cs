using Game.Config.Economy;

namespace Game.Engine.Tests;

/// <summary>
/// Чистые формулы износа (SPEC §5.6) — ускоряющийся декей, штраф к содержанию, выбор ступени
/// капремонта, порог критического состояния. Переход в простой и сами события — в <see
/// cref="WearStepTests"/>.
/// </summary>
public class WearCalculatorTests
{
    private static readonly OverhaulTierConfig LightTier = new()
    {
        Id = "light", Name = "Лёгкое обслуживание", MinCondition = 0.85m, CostFraction = 0.03m,
        DurationTurns = 1, OutputMultiplier = 0.95m, SalaryRate = 1m, UpkeepRate = 1m,
    };
    private static readonly OverhaulTierConfig MajorTier = new()
    {
        Id = "major", Name = "Капремонт", MinCondition = 0.5m, CostFraction = 0.15m,
        DurationTurns = 2, OutputMultiplier = 0m, SalaryRate = 0.66m, UpkeepRate = 0.5m,
    };
    private static readonly OverhaulTierConfig ReconstructionTier = new()
    {
        Id = "reconstruction", Name = "Полная реконструкция", MinCondition = 0.2m, CostFraction = 0.4m,
        DurationTurns = 5, OutputMultiplier = 0m, SalaryRate = 0.66m, UpkeepRate = 0.5m,
    };
    private static readonly IReadOnlyList<OverhaulTierConfig> Tiers = new[] { LightTier, MajorTier, ReconstructionTier };

    private static WearConfig NewConfig(
        int gracePeriodTurns = 5,
        decimal baseWearRatePerTurn = 0.05m,
        decimal accelerationFactorPerTurn = 0.01m,
        decimal maxUpkeepPenaltyMultiplier = 0.5m,
        decimal criticalConditionThreshold = 0.2m) => new WearConfig
    {
        GracePeriodTurns = gracePeriodTurns,
        BaseWearRatePerTurn = baseWearRatePerTurn,
        AccelerationFactorPerTurn = accelerationFactorPerTurn,
        MaxUpkeepPenaltyMultiplier = maxUpkeepPenaltyMultiplier,
        OverhaulTiers = Tiers,
        CriticalConditionThreshold = criticalConditionThreshold,
        ForcedRepairDurationTurns = 3,
        ForcedRepairSalaryRate = 0.66m,
        ForcedRepairUpkeepRate = 0.5m,
        PostForcedRepairCondition = 0.85m,
    };

    [Fact]
    public void CalculateAgeBeyondGrace_Is_NonPositive_During_The_Grace_Period()
    {
        // Построена на ходу 10, льгота 5 ходов — льгота действует по ход 15 включительно.
        Assert.Equal(-5, WearCalculator.CalculateAgeBeyondGrace(lastResetTurn: 10, currentTurn: 10, gracePeriodTurns: 5));
        Assert.Equal(0, WearCalculator.CalculateAgeBeyondGrace(lastResetTurn: 10, currentTurn: 15, gracePeriodTurns: 5));
        Assert.Equal(1, WearCalculator.CalculateAgeBeyondGrace(lastResetTurn: 10, currentTurn: 16, gracePeriodTurns: 5));
    }

    [Fact]
    public void CalculateDecayRate_Is_Zero_While_Still_Within_The_Grace_Period()
    {
        var config = NewConfig();

        Assert.Equal(0m, WearCalculator.CalculateDecayRate(ageBeyondGrace: -3, config));
        Assert.Equal(0m, WearCalculator.CalculateDecayRate(ageBeyondGrace: 0, config));
    }

    [Fact]
    public void CalculateDecayRate_Grows_Linearly_With_Age_Beyond_Grace()
    {
        var config = NewConfig(baseWearRatePerTurn: 0.05m, accelerationFactorPerTurn: 0.01m);

        // decayRate(t) = 0.05 + 0.01*t — незаметно сразу после льготы, дальше быстрее.
        Assert.Equal(0.06m, WearCalculator.CalculateDecayRate(ageBeyondGrace: 1, config));
        Assert.Equal(0.10m, WearCalculator.CalculateDecayRate(ageBeyondGrace: 5, config));
        Assert.Equal(0.15m, WearCalculator.CalculateDecayRate(ageBeyondGrace: 10, config));
    }

    [Fact]
    public void IsFullyRestored_Is_True_Only_At_Exactly_1()
    {
        Assert.True(WearCalculator.IsFullyRestored(1m));
        Assert.False(WearCalculator.IsFullyRestored(0.99m));
    }

    [Fact]
    public void CalculateNextCondition_Applies_Decay()
    {
        Assert.Equal(0.74m, WearCalculator.CalculateNextCondition(condition: 0.8m, decayRate: 0.06m));
    }

    [Fact]
    public void CalculateNextCondition_Clamps_To_The_0_To_1_Range()
    {
        Assert.Equal(0m, WearCalculator.CalculateNextCondition(condition: 0.02m, decayRate: 0.5m));
        Assert.Equal(1m, WearCalculator.CalculateNextCondition(condition: 1m, decayRate: -0.1m)); // декей отрицательным не бывает на практике, но клэмп симметричный
    }

    [Fact]
    public void CalculateUpkeepPenaltyMultiplier_Is_1_At_Perfect_Condition()
    {
        var config = NewConfig(criticalConditionThreshold: 0.2m, maxUpkeepPenaltyMultiplier: 0.5m);

        Assert.Equal(1m, WearCalculator.CalculateUpkeepPenaltyMultiplier(1m, config));
    }

    [Fact]
    public void CalculateUpkeepPenaltyMultiplier_Reaches_The_Configured_Maximum_At_The_Critical_Threshold()
    {
        var config = NewConfig(criticalConditionThreshold: 0.2m, maxUpkeepPenaltyMultiplier: 0.5m);

        Assert.Equal(1.5m, WearCalculator.CalculateUpkeepPenaltyMultiplier(0.2m, config));
    }

    [Fact]
    public void CalculateUpkeepPenaltyMultiplier_Interpolates_Linearly_Between_1_And_The_Threshold()
    {
        var config = NewConfig(criticalConditionThreshold: 0m, maxUpkeepPenaltyMultiplier: 0.5m);

        // На полпути между 1.0 и порогом 0 (Condition=0.5) — половина максимального штрафа.
        Assert.Equal(1.25m, WearCalculator.CalculateUpkeepPenaltyMultiplier(0.5m, config));
    }

    [Fact]
    public void IsCritical_Is_True_At_And_Below_The_Threshold()
    {
        var config = NewConfig(criticalConditionThreshold: 0.2m);

        Assert.True(WearCalculator.IsCritical(0.2m, config));
        Assert.True(WearCalculator.IsCritical(0.1m, config));
        Assert.False(WearCalculator.IsCritical(0.21m, config));
    }

    [Theory]
    [InlineData(1.0, "light")]
    [InlineData(0.9, "light")]
    [InlineData(0.85, "light")]
    [InlineData(0.84, "major")]
    [InlineData(0.5, "major")]
    [InlineData(0.49, "reconstruction")]
    [InlineData(0.2, "reconstruction")]
    public void SelectTier_Picks_The_First_Tier_Whose_Threshold_The_Condition_Satisfies(decimal condition, string expectedTierId)
    {
        var tier = WearCalculator.SelectTier(condition, Tiers);

        Assert.NotNull(tier);
        Assert.Equal(expectedTierId, tier!.Id);
    }

    [Fact]
    public void SelectTier_Returns_Null_Below_The_Lowest_Tier()
    {
        Assert.Null(WearCalculator.SelectTier(0.19m, Tiers));
    }
}
