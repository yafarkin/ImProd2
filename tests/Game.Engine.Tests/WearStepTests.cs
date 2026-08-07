using Game.Config.Catalog;
using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>
/// Один ход износа/капремонта/простоя одной фабрики (SPEC §5.6) — строит события, ничего не
/// применяет (см. doc-comment <see cref="WearStep"/>), тот же приём проверки, что и <see
/// cref="RndInvestmentStepTests"/>. Сами формулы декея/выбора ступени — отдельно, в <see
/// cref="WearCalculatorTests"/>.
/// </summary>
public class WearStepTests
{
    private static Factory NewFactory(Team team, int builtAtTurn = 0) =>
        team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine, builtAtTurn: builtAtTurn);

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
        decimal criticalConditionThreshold = 0.2m,
        int forcedRepairDurationTurns = 3,
        decimal forcedRepairSalaryRate = 0.66m,
        decimal forcedRepairUpkeepRate = 0.5m,
        decimal postForcedRepairCondition = 0.85m) => new WearConfig
    {
        GracePeriodTurns = gracePeriodTurns,
        BaseWearRatePerTurn = baseWearRatePerTurn,
        AccelerationFactorPerTurn = accelerationFactorPerTurn,
        MaxUpkeepPenaltyMultiplier = 0.5m,
        OverhaulTiers = Tiers,
        CriticalConditionThreshold = criticalConditionThreshold,
        ForcedRepairDurationTurns = forcedRepairDurationTurns,
        ForcedRepairSalaryRate = forcedRepairSalaryRate,
        ForcedRepairUpkeepRate = forcedRepairUpkeepRate,
        PostForcedRepairCondition = postForcedRepairCondition,
    };

    private static readonly WorkerProductivityConfig WorkerConfig = TestGameConfig.Resolved.Raw.WorkerProductivity;
    private static readonly IReadOnlyList<FactoryDefinitionConfig> FactoryDefinitions = TestGameConfig.Resolved.Raw.FactoryDefinitions;

    [Fact]
    public void Run_Returns_Nothing_During_The_Grace_Period()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = NewFactory(team, builtAtTurn: 1);
        var config = NewConfig(gracePeriodTurns: 10);

        var changes = WearStep.Run(team.Id, factory, currentTurn: 5, config, WorkerConfig, FactoryDefinitions);

        Assert.Empty(changes);
    }

    [Fact]
    public void Run_Emits_A_Condition_Change_After_The_Grace_Period_Elapses()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = NewFactory(team, builtAtTurn: 1);
        var config = NewConfig(gracePeriodTurns: 5, baseWearRatePerTurn: 0.05m, accelerationFactorPerTurn: 0.01m);

        // Ход 7: возраст сверх льготы = 7 - 1 - 5 = 1 -> decayRate = 0.05 + 0.01*1 = 0.06.
        var changes = WearStep.Run(team.Id, factory, currentTurn: 7, config, WorkerConfig, FactoryDefinitions);

        var changed = Assert.IsType<FactoryConditionChanged>(Assert.Single(changes));
        Assert.Equal(1m, changed.PreviousCondition);
        Assert.Equal(0.94m, changed.NewCondition);
        Assert.Equal(0.06m, changed.DecayApplied);
    }

    [Fact]
    public void Run_Emits_Entered_Repair_Instead_Of_A_Condition_Change_When_Crossing_The_Threshold()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = NewFactory(team, builtAtTurn: 1);
        factory.ApplyConditionChange(0.23m); // чуть выше порога 0.2
        var config = NewConfig(gracePeriodTurns: 0, baseWearRatePerTurn: 0.05m, accelerationFactorPerTurn: 0m, criticalConditionThreshold: 0.2m, forcedRepairDurationTurns: 4);

        // Возраст сверх льготы = 2 - 1 - 0 = 1 -> decayRate = 0.05 -> newCondition = 0.23 - 0.05 = 0.18 <= 0.2.
        var changes = WearStep.Run(team.Id, factory, currentTurn: 2, config, WorkerConfig, FactoryDefinitions);

        var entered = Assert.IsType<FactoryEnteredRepair>(Assert.Single(changes));
        Assert.Equal(0.18m, entered.ConditionAtEntry);
        Assert.Equal(4, entered.DurationTurns);
        Assert.Equal(0.66m, entered.SalaryRate);
        Assert.Equal(0.5m, entered.UpkeepRate);
        Assert.Equal(0.85m, entered.TargetCondition);
    }

    [Fact]
    public void Run_Emits_Overhaul_Started_When_Requested_Selecting_The_Matching_Tier()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = NewFactory(team, builtAtTurn: 1);
        factory.ApplyConditionChange(0.6m); // попадает в диапазон ступени "major" (0.5..0.85)
        factory.SetOverhaulRequested(true);
        var config = NewConfig();

        var changes = WearStep.Run(team.Id, factory, currentTurn: 10, config, WorkerConfig, FactoryDefinitions);

        var started = Assert.IsType<FactoryOverhaulStarted>(Assert.Single(changes));
        Assert.Equal("major", started.TierId);
        Assert.Equal("Капремонт", started.TierName);
        Assert.Equal(0.6m, started.ConditionAtStart);
        Assert.Equal(2, started.DurationTurns);
        Assert.Equal(0m, started.OutputMultiplier);
        Assert.Equal(0.66m, started.SalaryRate);
        Assert.Equal(0.5m, started.UpkeepRate);
    }

    [Fact]
    public void Run_Computes_The_Overhaul_Cost_As_A_Fraction_Of_The_Build_Cost()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = NewFactory(team, builtAtTurn: 1);
        factory.ApplyConditionChange(0.95m); // ступень "light", CostFraction=0.03
        factory.SetOverhaulRequested(true);
        var config = NewConfig();
        var buildCost = FactoryDefinitions.First(d => d.Id == TestGameConfig.Mine.Id).BuildCost;

        var changes = WearStep.Run(team.Id, factory, currentTurn: 10, config, WorkerConfig, FactoryDefinitions);

        var started = Assert.IsType<FactoryOverhaulStarted>(Assert.Single(changes));
        Assert.Equal("light", started.TierId);
        Assert.Equal(buildCost * 0.03m, started.Cost);
        Assert.Equal(1, started.DurationTurns);
        Assert.Equal(0.95m, started.OutputMultiplier);
    }

    [Fact]
    public void Run_Throws_When_Overhaul_Is_Requested_Below_The_Lowest_Tier()
    {
        // Не должно происходить на практике (GameSession не пускает такой запрос, а порог движка не
        // ниже нижней ступени) — но WearStep обязан упасть громко, а не молча всё сломать, если это
        // всё же произошло.
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = NewFactory(team, builtAtTurn: 1);
        factory.ApplyConditionChange(0.1m); // ниже нижней ступени (0.2)
        factory.SetOverhaulRequested(true);
        var config = NewConfig();

        Assert.Throws<InvalidOperationException>(() => WearStep.Run(team.Id, factory, currentTurn: 10, config, WorkerConfig, FactoryDefinitions));
    }

    [Fact]
    public void Run_Emits_Only_A_Repair_Turn_Passed_Event_While_Repair_Continues()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = NewFactory(team, builtAtTurn: 1);
        factory.Hire(4);
        factory.StartRepair(conditionAtEntry: 0.4m, durationTurns: 3, outputMultiplier: 0m, salaryRate: 0.66m, upkeepRate: 0.5m, targetCondition: 0.85m);
        var config = NewConfig();
        var factoryDefinitions = new[]
        {
            new FactoryDefinitionConfig
            {
                Id = TestGameConfig.Mine.Id, Name = "Рудник", SectorId = TestGameConfig.Mine.Sector.Id,
                RecipeIds = new[] { TestGameConfig.Mine.Recipes[0].Id }, BuildCost = 100m,
                LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 20m,
            },
        };

        var changes = WearStep.Run(team.Id, factory, currentTurn: 10, config, WorkerConfig, factoryDefinitions);

        var passed = Assert.IsType<FactoryRepairTurnPassed>(Assert.Single(changes));
        Assert.Equal(2, passed.TurnsRemainingAfter);
        Assert.Equal(4 * WorkerConfig.SalaryPerWorkerPerTurn * 0.66m, passed.SalaryPaid);
        Assert.Equal(20m * 0.5m, passed.UpkeepPaid);
    }

    [Fact]
    public void Run_Uses_The_Rates_Captured_At_The_Start_Of_Repair_Not_The_Configs_Forced_Repair_Rates()
    {
        // Лёгкая ступень капремонта — 100% зарплаты/содержания, а не льготный тариф вынужденного
        // простоя (0.66/0.5 в конфиге) — подтверждаем, что WearStep читает захваченные на фабрике
        // значения, а не константы конфига.
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = NewFactory(team, builtAtTurn: 1);
        factory.Hire(4);
        factory.StartRepair(conditionAtEntry: 0.9m, durationTurns: 1, outputMultiplier: 0.95m, salaryRate: 1m, upkeepRate: 1m, targetCondition: 1m);
        var config = NewConfig();
        var factoryDefinitions = new[]
        {
            new FactoryDefinitionConfig
            {
                Id = TestGameConfig.Mine.Id, Name = "Рудник", SectorId = TestGameConfig.Mine.Sector.Id,
                RecipeIds = new[] { TestGameConfig.Mine.Recipes[0].Id }, BuildCost = 100m,
                LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 20m,
            },
        };

        var changes = WearStep.Run(team.Id, factory, currentTurn: 10, config, WorkerConfig, factoryDefinitions);

        Assert.Equal(2, changes.Count); // 1-ходовая ступень завершается в этот же вызов
        var passed = Assert.IsType<FactoryRepairTurnPassed>(changes[0]);
        Assert.Equal(4 * WorkerConfig.SalaryPerWorkerPerTurn, passed.SalaryPaid); // 100%, не 66%
        Assert.Equal(20m, passed.UpkeepPaid); // 100%, не 50%
    }

    [Fact]
    public void Run_Also_Emits_Repair_Completed_On_The_Last_Turn_Of_Repair()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = NewFactory(team, builtAtTurn: 1);
        factory.StartRepair(conditionAtEntry: 0.4m, durationTurns: 1, outputMultiplier: 0m, salaryRate: 0.66m, upkeepRate: 0.5m, targetCondition: 0.85m);
        var config = NewConfig();

        var changes = WearStep.Run(team.Id, factory, currentTurn: 12, config, WorkerConfig, FactoryDefinitions);

        Assert.Equal(2, changes.Count);
        var passed = Assert.IsType<FactoryRepairTurnPassed>(changes[0]);
        Assert.Equal(0, passed.TurnsRemainingAfter);
        var completed = Assert.IsType<FactoryRepairCompleted>(changes[1]);
        Assert.Equal(0.85m, completed.NewCondition);
        Assert.Equal(12, completed.Turn);
    }

    [Fact]
    public void Run_Also_Emits_Repair_Completed_At_1_For_A_Voluntary_Overhaul()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = NewFactory(team, builtAtTurn: 1);
        factory.StartRepair(conditionAtEntry: 0.6m, durationTurns: 1, outputMultiplier: 0m, salaryRate: 0.66m, upkeepRate: 0.5m, targetCondition: 1m);
        var config = NewConfig();

        var changes = WearStep.Run(team.Id, factory, currentTurn: 12, config, WorkerConfig, FactoryDefinitions);

        var completed = Assert.IsType<FactoryRepairCompleted>(changes[1]);
        Assert.Equal(1m, completed.NewCondition); // капремонт всегда восстанавливает до 100%, в отличие от вынужденного простоя
    }

    [Fact]
    public void Run_Does_Not_Apply_Decay_Or_Start_New_Overhauls_While_A_Factory_Is_Under_Repair()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = NewFactory(team, builtAtTurn: 1);
        factory.SetOverhaulRequested(true); // объявлен до простоя — во время простоя игнорируется
        factory.StartRepair(conditionAtEntry: 0.4m, durationTurns: 2, outputMultiplier: 0m, salaryRate: 0.66m, upkeepRate: 0.5m, targetCondition: 0.85m);
        var config = NewConfig(baseWearRatePerTurn: 0.9m); // намеренно огромный декей — не должен примениться

        var changes = WearStep.Run(team.Id, factory, currentTurn: 20, config, WorkerConfig, FactoryDefinitions);

        Assert.DoesNotContain(changes, c => c is FactoryConditionChanged or FactoryOverhaulStarted);
    }
}
