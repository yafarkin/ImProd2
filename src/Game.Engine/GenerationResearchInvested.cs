namespace Game.Engine;

/// <summary>
/// Команда вложила деньги в исследование следующего поколения фабрик — решение команды, отдельное
/// от возможного следствия (перехода поколения, см. <see cref="TeamGenerationAdvanced"/>): вложение
/// может не дотянуть до порога следующего поколения, и это тоже факт, достойный своей записи (тот
/// же приём, что <see cref="RndInvested"/> для одной фабрики).
/// </summary>
public sealed record GenerationResearchInvested : Change<GameSessionState>
{
    /// <summary>Команда, сделавшая вложение.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Сумма вложения.</summary>
    public required decimal Amount { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        team.InvestInGenerationResearch(Amount);
    }
}
