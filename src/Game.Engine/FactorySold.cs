using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Команда продала (ликвидировала) построенную фабрику — мгновенно и необратимо, симметрично
/// <see cref="FactoryBuilt"/> (SPEC §5.6/§5.11, запрос пользователя: «в реальном бизнесе фабрику
/// можно продать, а не только построить»): фабрика перестаёт существовать со следующего же расчёта,
/// без отдельного «отложенного» состояния — тем же приёмом, что и постройка. Не нуждается в
/// decision/settlement split (SPEC §4) — продажа не конкурирует ни за какой общий ресурс хода между
/// командами, в отличие, например, от продажи сырья системе. <see cref="Amount"/> — та же
/// ликвидационная стоимость (<c>BuildCost * LiquidationValueCoefficient</c>), что уже показывает
/// итоговый счёт в конце игры (<see cref="FinalScoreCalculator"/>) — команда получает сейчас ровно
/// то, что до этого было лишь оценкой на бумаге. Рабочие проданной фабрики перестают числиться вместе
/// с ней, без отдельного события увольнения (то же упрощение, что и у постройки).
/// </summary>
public sealed record FactorySold : Change<GameSessionState>
{
    /// <summary>Команда, продавшая фабрику.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Идентификатор проданной фабрики.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Тип проданной фабрики (<c>FactoryDefinitionConfig.Id</c>) — для истории/аудита, сама фабрика к моменту чтения истории уже не существует в состоянии.</summary>
    public required string FactoryDefinitionId { get; init; }

    /// <summary>Полученная сумма — ликвидационная стоимость на момент продажи.</summary>
    public required decimal Amount { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        team.RemoveFactory(FactoryId);
        if (Amount > 0)
        {
            team.Credit(Amount);
        }
    }
}
