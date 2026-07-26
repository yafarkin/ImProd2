using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Участник зарегистрирован в сессии под коротким кодом входа (Блок 8.1, SPEC §3). Не действие
/// команды и не привязано к фазе решений — это настройка сессии, тот же класс событий, что и
/// <see cref="NewsPublished"/> при ручной публикации ведущим.
/// </summary>
public sealed record ParticipantRegistered : Change<GameSessionState>
{
    /// <summary>Код входа участника.</summary>
    public required string Code { get; init; }

    /// <summary>Роль участника.</summary>
    public required ParticipantRole Role { get; init; }

    /// <summary>Команда участника — только для ролей, привязанных к команде (см. <see cref="ParticipantRegistration"/>).</summary>
    public required Ulid? TeamId { get; init; }

    /// <summary>Отображаемое имя участника.</summary>
    public required string DisplayName { get; init; }

    public override void Apply(GameSessionState state)
    {
        state.AddParticipant(new ParticipantRegistration(Code, Role, TeamId, DisplayName));
    }
}
