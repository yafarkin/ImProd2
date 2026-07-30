using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Участник добавлен в черновик до старта сессии и сразу получил код входа (Блок 9.8) — код,
/// пришедший в событии, уже выдан и провалидирован (см. <see cref="ParticipantRegistration"/>)
/// вызывающим кодом до <c>Append</c>; <see cref="Apply"/> — чистая, не бросающая мутация.
/// </summary>
public sealed record ParticipantStaged : Change<DraftState>
{
    /// <summary>Идентификатор записи в черновике.</summary>
    public required Ulid ParticipantId { get; init; }

    /// <summary>Код входа участника.</summary>
    public required string Code { get; init; }

    /// <summary>Роль участника.</summary>
    public required ParticipantRole Role { get; init; }

    /// <summary>Команда участника — только для ролей, привязанных к команде.</summary>
    public required Ulid? TeamId { get; init; }

    /// <summary>Отображаемое имя участника.</summary>
    public required string DisplayName { get; init; }

    public override void Apply(DraftState state)
    {
        state.AddParticipant(new StagedParticipantSpec(ParticipantId, Code, Role, TeamId, DisplayName));
    }
}
