namespace Game.Domain;

/// <summary>
/// Участник сессии, зарегистрированный под коротким кодом входа (Блок 8.1, SPEC §3): код, роль,
/// команда (только для ролей, привязанных к команде) и отображаемое имя.
/// </summary>
public sealed record ParticipantRegistration
{
    /// <summary>Код входа участника.</summary>
    public string Code { get; }

    /// <summary>Роль участника.</summary>
    public ParticipantRole Role { get; }

    /// <summary>
    /// Команда участника — обязательна для <see cref="ParticipantRole.Manager"/> и
    /// <see cref="ParticipantRole.Negotiator"/>, и обязана быть null для остальных ролей (они не
    /// привязаны к команде, SPEC §3).
    /// </summary>
    public Ulid? TeamId { get; }

    /// <summary>Отображаемое имя участника.</summary>
    public string DisplayName { get; }

    public ParticipantRegistration(string code, ParticipantRole role, Ulid? teamId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Code must not be empty.", nameof(code));
        }
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name must not be empty.", nameof(displayName));
        }

        var isTeamScoped = role is ParticipantRole.Manager or ParticipantRole.Negotiator;
        if (isTeamScoped && teamId is null)
        {
            throw new ArgumentException($"Role '{role}' requires a team.", nameof(teamId));
        }
        if (!isTeamScoped && teamId is not null)
        {
            throw new ArgumentException($"Role '{role}' must not be bound to a team.", nameof(teamId));
        }

        Code = code;
        Role = role;
        TeamId = teamId;
        DisplayName = displayName;
    }
}
