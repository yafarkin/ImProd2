using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Фабрика произвела продукцию за тик: входы списаны, выход зачислен на склад команды (SPEC §5.6).
/// Несёт уже вычисленные <see cref="ProductionCalculator.Calculate"/> величины, а не пересчитывает
/// их заново при применении — воспроизведение из журнала не должно зависеть от того, что конфиг
/// производительности рабочих не изменился (AGENTS §2, правило 6), и это тот же факт, который стоит
/// один раз посчитать и один раз записать. <see cref="CapacityLimitedOutputQuantity"/> отдельно от
/// <see cref="OutputQuantity"/> показывает, было ли производство ограничено нехваткой сырья, а не
/// просто мощностью — не нужно пересчитывать это по соседним записям склада (AGENTS-память о
/// трассируемости причин). <see cref="LaborCost"/> — зарплата, отнесённая на эту фабрику за этот
/// ход (не задваивает списание баланса — оно уже разово происходит через <see cref="SalariesPaid"/>,
/// здесь это чисто бухгалтерская привязка реальной стоимости к конкретной партии товара на складе,
/// см. <see cref="Domain.MaterialOnStock"/>), нужна вместе со списанной реальной себестоимостью
/// входов, чтобы посчитать реальную (не рыночную) себестоимость новой партии выхода.
/// </summary>
public sealed record FactoryProduced : Change<GameSessionState>
{
    /// <summary>Команда, на фабрике которой произошло производство.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Фабрика, на которой произошло производство.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Сколько удалось бы произвести исходя только из мощности, без учёта сырья.</summary>
    public required decimal CapacityLimitedOutputQuantity { get; init; }

    /// <summary>Сколько произведено фактически.</summary>
    public required decimal OutputQuantity { get; init; }

    /// <summary>Фактически списанное количество каждого входного материала (код материала → количество).</summary>
    public required IReadOnlyDictionary<string, decimal> ConsumedInputs { get; init; }

    /// <summary>Зарплата, отнесённая на эту фабрику за этот ход (число рабочих × ставка за ход).</summary>
    public required decimal LaborCost { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        var factory = team.Factories.Single(f => f.Id == FactoryId);
        var recipe = factory.SelectedRecipe;

        var consumedCostBasis = 0m;
        foreach (var (materialId, quantity) in ConsumedInputs)
        {
            if (quantity <= 0)
            {
                continue;
            }

            var material = recipe.Inputs.First(input => input.Material.Id == materialId).Material;
            consumedCostBasis += team.Warehouse.Remove(material, quantity);
        }

        // Если выхода в этот ход нет (простой из-за нехватки сырья), LaborCost и consumedCostBasis
        // просто пропадают — деньги уже потрачены (зарплата, а не эта партия), а товара, на который
        // их можно было бы отнести, не появилось. Так же и в жизни.
        if (OutputQuantity > 0)
        {
            team.Warehouse.Add(recipe.Output, OutputQuantity, LaborCost + consumedCostBasis);
        }
    }
}
