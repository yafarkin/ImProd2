using Game.Domain;

namespace Game.Engine;

/// <summary>Команда опубликовала запись на доске потребностей (Блок 9.4, SPEC §9.2).</summary>
public sealed record NeedPosted : Change<GameSessionState>
{
    /// <summary>Команда, опубликовавшая запись.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Идентификатор записи.</summary>
    public required Ulid NeedId { get; init; }

    /// <summary>Код материала.</summary>
    public required string MaterialId { get; init; }

    /// <summary>Избыток или дефицит.</summary>
    public required NeedDirection Direction { get; init; }

    /// <summary>Грубый порядок объёма.</summary>
    public required NeedVolumeOrder VolumeOrder { get; init; }

    /// <summary>Необязательный комментарий.</summary>
    public string? Comment { get; init; }

    public override void Apply(GameSessionState state)
    {
        var material = state.Config.Materials[MaterialId];
        state.AddNeed(new NeedPosting(NeedId, TeamId, material, Direction, VolumeOrder, Comment));
    }
}
