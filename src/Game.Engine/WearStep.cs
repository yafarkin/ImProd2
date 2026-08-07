using Game.Config.Catalog;
using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Один ход износа/капремонта/простоя одной фабрики (SPEC §5.6) — тот же приём «чистая функция,
/// строящая события, ничего не применяет», что и <see cref="RndInvestmentStep"/>/<see
/// cref="WorkforceStep"/>. Три взаимоисключающих случая для одной фабрики за один ход: (1) уже в
/// простое — считает только сам простой, декей и новые запросы капремонта не идут; (2) команда
/// запросила капремонт (<see cref="Factory.OverhaulRequested"/>) — по текущему состоянию выбирается
/// ступень (см. <see cref="WearCalculator.SelectTier"/>), запускается её эффект, декей этого хода не
/// считается; (3) иначе — рутинный декей, и если результат пересёк критический порог, вместо
/// рутинного изменения эмитится вынужденный простой (safety net). Возвращает 0-2 события за вызов
/// (второе — только в ход завершения простоя).
/// </summary>
public static class WearStep
{
    public static IReadOnlyList<Change<GameSessionState>> Run(
        Ulid teamId, Factory factory, int currentTurn, WearConfig wearConfig,
        WorkerProductivityConfig workerConfig, IReadOnlyList<FactoryDefinitionConfig> factoryDefinitions)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(wearConfig);
        ArgumentNullException.ThrowIfNull(workerConfig);
        ArgumentNullException.ThrowIfNull(factoryDefinitions);

        if (factory.IsUnderRepair)
        {
            return RunRepairTurn(teamId, factory, currentTurn, workerConfig, factoryDefinitions);
        }

        if (factory.OverhaulRequested)
        {
            return RunOverhaulRequest(teamId, factory, factoryDefinitions, wearConfig);
        }

        return RunRoutineTurn(teamId, factory, currentTurn, wearConfig);
    }

    private static IReadOnlyList<Change<GameSessionState>> RunRepairTurn(
        Ulid teamId, Factory factory, int currentTurn,
        WorkerProductivityConfig workerConfig, IReadOnlyList<FactoryDefinitionConfig> factoryDefinitions)
    {
        var definition = factoryDefinitions.First(d => d.Id == factory.Definition.Id);

        // Плоский тариф от базовой ставки за рабочего, а не через прогрессивную командную кривую
        // зарплаты (FinanceCalculator.CalculateSalaries) — простой одной фабрики не должен зависеть от
        // порядка учёта долей внутри общего расчёта по команде, см. doc-comment FactoryRepairTurnPassed.
        // Тарифы — зафиксированные при начале именно этого простоя (StartRepair), не константы
        // конфига: у лёгкой ступени капремонта они могут быть 100% (фабрика фактически работает).
        var salaryPaid = factory.Workers * workerConfig.SalaryPerWorkerPerTurn * factory.RepairSalaryRate;
        var upkeepPaid = definition.FixedCostPerTurn * factory.RepairUpkeepRate;
        var turnsRemainingAfter = factory.RepairTurnsRemaining - 1;

        var changes = new List<Change<GameSessionState>>
        {
            new FactoryRepairTurnPassed
            {
                Id = Ulid.NewUlid(),
                TeamId = teamId,
                FactoryId = factory.Id,
                TurnsRemainingAfter = turnsRemainingAfter,
                SalaryPaid = salaryPaid,
                UpkeepPaid = upkeepPaid,
            },
        };

        if (turnsRemainingAfter <= 0)
        {
            changes.Add(new FactoryRepairCompleted
            {
                Id = Ulid.NewUlid(),
                TeamId = teamId,
                FactoryId = factory.Id,
                NewCondition = factory.RepairTargetCondition,
                Turn = currentTurn,
            });
        }

        return changes;
    }

    private static IReadOnlyList<Change<GameSessionState>> RunOverhaulRequest(
        Ulid teamId, Factory factory, IReadOnlyList<FactoryDefinitionConfig> factoryDefinitions, WearConfig wearConfig)
    {
        var tier = WearCalculator.SelectTier(factory.Condition, wearConfig.OverhaulTiers)
                   ?? throw new InvalidOperationException(
                       $"Factory '{factory.Id}' requested an overhaul at condition {factory.Condition}, but no configured tier covers it — " +
                       $"WearConfig.OverhaulTiers must cover the whole range down to CriticalConditionThreshold.");

        var buildCost = factoryDefinitions.First(d => d.Id == factory.Definition.Id).BuildCost;

        return new Change<GameSessionState>[]
        {
            new FactoryOverhaulStarted
            {
                Id = Ulid.NewUlid(),
                TeamId = teamId,
                FactoryId = factory.Id,
                TierId = tier.Id,
                TierName = tier.Name,
                ConditionAtStart = factory.Condition,
                Cost = buildCost * tier.CostFraction,
                DurationTurns = tier.DurationTurns,
                OutputMultiplier = tier.OutputMultiplier,
                SalaryRate = tier.SalaryRate,
                UpkeepRate = tier.UpkeepRate,
            },
        };
    }

    private static IReadOnlyList<Change<GameSessionState>> RunRoutineTurn(Ulid teamId, Factory factory, int currentTurn, WearConfig wearConfig)
    {
        var ageBeyondGrace = WearCalculator.CalculateAgeBeyondGrace(factory.LastResetTurn, currentTurn, wearConfig.GracePeriodTurns);
        var decayRate = WearCalculator.CalculateDecayRate(ageBeyondGrace, wearConfig);
        var newCondition = WearCalculator.CalculateNextCondition(factory.Condition, decayRate);

        if (WearCalculator.IsCritical(newCondition, wearConfig))
        {
            return new Change<GameSessionState>[]
            {
                new FactoryEnteredRepair
                {
                    Id = Ulid.NewUlid(),
                    TeamId = teamId,
                    FactoryId = factory.Id,
                    ConditionAtEntry = newCondition,
                    DurationTurns = wearConfig.ForcedRepairDurationTurns,
                    SalaryRate = wearConfig.ForcedRepairSalaryRate,
                    UpkeepRate = wearConfig.ForcedRepairUpkeepRate,
                    TargetCondition = wearConfig.PostForcedRepairCondition,
                },
            };
        }

        if (newCondition == factory.Condition)
        {
            // Льготный период — журнал не засоряем бесполезной записью.
            return Array.Empty<Change<GameSessionState>>();
        }

        return new Change<GameSessionState>[]
        {
            new FactoryConditionChanged
            {
                Id = Ulid.NewUlid(),
                TeamId = teamId,
                FactoryId = factory.Id,
                PreviousCondition = factory.Condition,
                NewCondition = newCondition,
                DecayApplied = decayRate,
            },
        };
    }
}
