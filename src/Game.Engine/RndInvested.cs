namespace Game.Engine;

/// <summary>
/// Команда вложила деньги в R&amp;D конкретной фабрики (SPEC §5.8) — решение команды, отдельное от
/// возможного следствия (перехода уровня, см. <see cref="FactoryLevelAdvanced"/>): вложение может
/// не дотянуть до порога следующего уровня, и это тоже факт, достойный своей записи.
/// </summary>
public sealed record RndInvested : Change<GameSessionState>
{
    /// <summary>Команда, сделавшая вложение.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Фабрика, в которую вложены деньги.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Сумма вложения.</summary>
    public required decimal Amount { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        var factory = team.Factories.Single(f => f.Id == FactoryId);
        team.Debit(Amount);
        factory.InvestInRnd(Amount);
    }
}
