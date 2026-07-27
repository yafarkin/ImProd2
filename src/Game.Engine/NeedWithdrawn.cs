namespace Game.Engine;

/// <summary>Команда отозвала свою запись с доски потребностей (Блок 9.4, SPEC §9.2).</summary>
public sealed record NeedWithdrawn : Change<GameSessionState>
{
    /// <summary>Отзываемая запись.</summary>
    public required Ulid NeedId { get; init; }

    public override void Apply(GameSessionState state) => state.Needs[NeedId].Withdraw();
}
