using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Состояние черновика настройки сессии (Блок 9.8) — команды и персонал до того, как администратор
/// нажал «Начать сессию». Выбранный конфиг сюда намеренно не входит: как и <see cref="GameSessionState.Config"/>,
/// он не проходит через <c>System.Text.Json</c> напрямую (объектный граф разрешённого каталога
/// этого не позволяет — конфиг восстанавливается только через <c>GameConfigLoader.Load</c> поверх
/// сырого JSON) — <c>GameSessionHost</c> хранит его как отдельный, независимо персистируемый файл,
/// а не событие. Наполняется событиями <see cref="TeamStaged"/>, <see cref="TeamUnstaged"/>,
/// <see cref="ParticipantStaged"/>, <see cref="ParticipantUnstaged"/>.
/// </summary>
public sealed class DraftState
{
    private readonly Dictionary<Ulid, StagedTeamSpec> _teams = new();

    /// <summary>Команды черновика по идентификатору — наполняется событием <see cref="TeamStaged"/>.</summary>
    public IReadOnlyDictionary<Ulid, StagedTeamSpec> Teams => _teams;

    private readonly Dictionary<Ulid, StagedParticipantSpec> _participants = new();

    /// <summary>Персонал черновика по идентификатору — наполняется событием <see cref="ParticipantStaged"/>.</summary>
    public IReadOnlyDictionary<Ulid, StagedParticipantSpec> Participants => _participants;

    /// <summary>Добавляет команду в черновик; вызывается только из <see cref="TeamStaged.Apply"/>.</summary>
    internal void AddTeam(StagedTeamSpec team)
    {
        _teams.Add(team.Id, team);
    }

    /// <summary>
    /// Убирает команду из черновика вместе с её застейдженным управляющим (без своей команды его
    /// регистрация не имеет смысла — см. <see cref="ParticipantRegistration"/>). Вызывается
    /// только из <see cref="TeamUnstaged.Apply"/>.
    /// </summary>
    internal void RemoveTeam(Ulid id)
    {
        _teams.Remove(id);
        foreach (var participantId in _participants.Values.Where(p => p.TeamId == id).Select(p => p.Id).ToList())
        {
            _participants.Remove(participantId);
        }
    }

    /// <summary>Добавляет участника в черновик; вызывается только из <see cref="ParticipantStaged.Apply"/>.</summary>
    internal void AddParticipant(StagedParticipantSpec participant)
    {
        _participants.Add(participant.Id, participant);
    }

    /// <summary>Убирает участника из черновика; вызывается только из <see cref="ParticipantUnstaged.Apply"/>.</summary>
    internal void RemoveParticipant(Ulid id)
    {
        _participants.Remove(id);
    }
}

/// <summary>Одна команда в черновике до старта сессии — см. <see cref="DraftState.Teams"/>.</summary>
public sealed record StagedTeamSpec(Ulid Id, string Name, string SectorId);

/// <summary>Один участник в черновике до старта сессии — см. <see cref="DraftState.Participants"/>.</summary>
public sealed record StagedParticipantSpec(Ulid Id, string Code, ParticipantRole Role, Ulid? TeamId, string DisplayName);
