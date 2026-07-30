namespace Game.Engine;

/// <summary>Команда добавлена в черновик до старта сессии (Блок 9.8, экран настройки).</summary>
public sealed record TeamStaged : Change<DraftState>
{
    /// <summary>Идентификатор команды.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Отображаемое имя команды.</summary>
    public required string Name { get; init; }

    /// <summary>Код сектора команды (<see cref="Game.Config.Catalog.SectorConfig.Id"/>).</summary>
    public required string SectorId { get; init; }

    public override void Apply(DraftState state)
    {
        state.AddTeam(new StagedTeamSpec(TeamId, Name, SectorId));
    }
}
