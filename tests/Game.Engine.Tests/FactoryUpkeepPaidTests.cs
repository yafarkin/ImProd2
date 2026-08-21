using Game.Config.Loading;
using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>
/// Капитальные затраты на построенные фабрики (Блок 9.2, запрос пользователя: «платим за фабрику,
/// даже если она вообще не работает») — фиксированная часть, списываемая в <see cref="TickFinanceStep"/>.
/// Переменная часть (энергия, растёт вместе с объёмом выпуска) — отдельно, см.
/// <see cref="FactoryProducedTests"/>.
/// </summary>
public class FactoryUpkeepPaidTests
{
    [Fact]
    public void Run_Charges_Upkeep_For_An_Idle_Factory_With_No_Workers()
    {
        var config = TestGameConfig.BuildWithFactoryUpkeep(fixedCostPerTurn: 10m);
        var (log, team) = StartSessionWithOneTeam(config);
        team.BuildFactory(Ulid.NewUlid(), config.FactoryDefinitions.Single(f => f.Id == "iron-mine"));
        team.Credit(100m); // с запасом — иначе апкип уведёт баланс в минус

        var changes = TickFinanceStep.Run(
            team, config.Raw.WorkerProductivity, config.Raw.Warehouse,
            config.Raw.FactoryDefinitions, config.Raw.Rnd, config.Raw.GenerationResearch, wearConfig: config.Raw.Wear, currentTurn: 1);

        var upkeep = Assert.IsType<FactoryUpkeepPaid>(Assert.Single(changes));
        Assert.Equal(10m, upkeep.Amount); // 0 рабочих, ноль зарплаты — но апкип платится в любом случае
        Assert.Equal(1, upkeep.FactoryCount);
    }

    [Fact]
    public void Run_Sums_Upkeep_Across_Several_Built_Factories()
    {
        var config = TestGameConfig.BuildWithFactoryUpkeep(fixedCostPerTurn: 10m);
        var (log, team) = StartSessionWithOneTeam(config);
        team.BuildFactory(Ulid.NewUlid(), config.FactoryDefinitions.Single(f => f.Id == "iron-mine"));
        team.BuildFactory(Ulid.NewUlid(), config.FactoryDefinitions.Single(f => f.Id == "steel-mill"));
        team.Credit(100m); // с запасом — иначе апкип уведёт баланс в минус

        var changes = TickFinanceStep.Run(
            team, config.Raw.WorkerProductivity, config.Raw.Warehouse,
            config.Raw.FactoryDefinitions, config.Raw.Rnd, config.Raw.GenerationResearch, wearConfig: config.Raw.Wear, currentTurn: 1);

        var upkeep = Assert.IsType<FactoryUpkeepPaid>(Assert.Single(changes));
        Assert.Equal(20m, upkeep.Amount); // 10 + 10 — два типа, у обоих FixedCostPerTurn=10 в этом варианте конфига
        Assert.Equal(2, upkeep.FactoryCount);
    }

    [Fact]
    public void Run_Charges_No_Upkeep_When_No_Factory_Is_Built()
    {
        var config = TestGameConfig.BuildWithFactoryUpkeep(fixedCostPerTurn: 10m);
        var (log, team) = StartSessionWithOneTeam(config);

        var changes = TickFinanceStep.Run(
            team, config.Raw.WorkerProductivity, config.Raw.Warehouse,
            config.Raw.FactoryDefinitions, config.Raw.Rnd, config.Raw.GenerationResearch, wearConfig: config.Raw.Wear, currentTurn: 1);

        Assert.Empty(changes);
    }

    [Fact]
    public void Run_Charges_Upkeep_Between_Salaries_And_The_Warehouse_Fee()
    {
        var config = TestGameConfig.BuildWithFactoryUpkeep(fixedCostPerTurn: 10m);
        var (log, team) = StartSessionWithOneTeam(config);
        var factory = team.BuildFactory(Ulid.NewUlid(), config.FactoryDefinitions.Single(f => f.Id == "iron-mine"));
        factory.Hire(1); // зарплата > 0, чтобы увидеть оба события и их порядок
        team.Credit(100m); // с запасом — иначе апкип уведёт баланс в минус

        var changes = TickFinanceStep.Run(
            team, config.Raw.WorkerProductivity, config.Raw.Warehouse,
            config.Raw.FactoryDefinitions, config.Raw.Rnd, config.Raw.GenerationResearch, wearConfig: config.Raw.Wear, currentTurn: 1);

        Assert.Equal(2, changes.Count);
        Assert.IsType<SalariesPaid>(changes[0]);
        Assert.IsType<FactoryUpkeepPaid>(changes[1]);
    }

    [Fact]
    public void Run_Scales_Upkeep_Up_As_A_Factory_Wears_Down()
    {
        // TestGameConfig.Wear: CriticalConditionThreshold=0.2, MaxUpkeepPenaltyMultiplier=0.5 — на
        // полпути между 1.0 и порогом (Condition=0.6) штраф на полпути к максимуму (×1.25).
        var config = TestGameConfig.BuildWithFactoryUpkeep(fixedCostPerTurn: 10m);
        var (log, team) = StartSessionWithOneTeam(config);
        var factory = team.BuildFactory(Ulid.NewUlid(), config.FactoryDefinitions.Single(f => f.Id == "iron-mine"));
        factory.ApplyConditionChange(0.6m);
        team.Credit(100m);

        var changes = TickFinanceStep.Run(
            team, config.Raw.WorkerProductivity, config.Raw.Warehouse,
            config.Raw.FactoryDefinitions, config.Raw.Rnd, config.Raw.GenerationResearch, wearConfig: config.Raw.Wear, currentTurn: 1);

        var upkeep = Assert.IsType<FactoryUpkeepPaid>(Assert.Single(changes, c => c is FactoryUpkeepPaid));
        Assert.Equal(12.5m, upkeep.Amount);
    }

    [Fact]
    public void Run_Excludes_A_Factory_Under_Forced_Repair_From_Upkeep()
    {
        // Содержание фабрики на вынужденном простое списывается отдельно, по льготному тарифу простоя
        // (см. WearStepTests) — второй штраф поверх был бы избыточен.
        var config = TestGameConfig.BuildWithFactoryUpkeep(fixedCostPerTurn: 10m);
        var (log, team) = StartSessionWithOneTeam(config);
        var factory = team.BuildFactory(Ulid.NewUlid(), config.FactoryDefinitions.Single(f => f.Id == "iron-mine"));
        factory.StartRepair(conditionAtEntry: 0.4m, durationTurns: 3, outputMultiplier: 0m, salaryRate: 0.66m, upkeepRate: 0.5m, targetCondition: 0.85m);
        team.Credit(100m);

        var changes = TickFinanceStep.Run(
            team, config.Raw.WorkerProductivity, config.Raw.Warehouse,
            config.Raw.FactoryDefinitions, config.Raw.Rnd, config.Raw.GenerationResearch, wearConfig: config.Raw.Wear, currentTurn: 1);

        Assert.DoesNotContain(changes, c => c is FactoryUpkeepPaid);
        Assert.Single(changes, c => c is FactoryRepairTurnPassed);
    }

    /// <summary>По образцу <see cref="TestGameConfig.StartSessionWithOneTeam"/>, но для произвольного конфига (не только <see cref="TestGameConfig.Resolved"/>).</summary>
    private static (EventLog<GameSessionState> Log, Team Team) StartSessionWithOneTeam(ResolvedGameConfig config)
    {
        var state = new GameSessionState(config);
        var log = new EventLog<GameSessionState>(state);
        var teamId = Ulid.NewUlid();

        log.Append(new SessionStarted
        {
            Id = Ulid.NewUlid(),
            PresetId = "test",
            EndTurn = 999,
            ConfigHash = config.ContentHash,
            Teams = new[]
            {
                new TeamSpec { Id = teamId, Name = "Команда А1", SectorId = config.Sectors[0].Id },
            },
        });

        return (log, state.Teams[teamId]);
    }
}
