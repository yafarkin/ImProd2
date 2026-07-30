using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>Черновик настройки сессии до старта (Блок 9.8) — события <see cref="DraftState"/>.</summary>
public class DraftStateTests
{
    private static EventLog<DraftState> NewLog() => new(new DraftState());

    [Fact]
    public void TeamStaged_Adds_A_Team_To_The_Draft()
    {
        var log = NewLog();
        var teamId = Ulid.NewUlid();

        log.Append(new TeamStaged { Id = Ulid.NewUlid(), TeamId = teamId, Name = "Команда А1", SectorId = TestGameConfig.SectorA.Id });

        var team = Assert.Single(log.State.Teams.Values);
        Assert.Equal(teamId, team.Id);
        Assert.Equal("Команда А1", team.Name);
        Assert.Equal(TestGameConfig.SectorA.Id, team.SectorId);
    }

    [Fact]
    public void TeamUnstaged_Removes_The_Team_And_Its_Staged_Manager()
    {
        var log = NewLog();
        var teamId = Ulid.NewUlid();
        var managerId = Ulid.NewUlid();
        var operatorId = Ulid.NewUlid();

        log.Append(new TeamStaged { Id = Ulid.NewUlid(), TeamId = teamId, Name = "Команда А1", SectorId = TestGameConfig.SectorA.Id });
        log.Append(new ParticipantStaged
        {
            Id = Ulid.NewUlid(), ParticipantId = managerId, Code = "MANAGR", Role = ParticipantRole.Manager,
            TeamId = teamId, DisplayName = "Управляющий",
        });
        log.Append(new ParticipantStaged
        {
            Id = Ulid.NewUlid(), ParticipantId = operatorId, Code = "OPERAT", Role = ParticipantRole.Operator,
            TeamId = null, DisplayName = "Оператор",
        });

        log.Append(new TeamUnstaged { Id = Ulid.NewUlid(), TeamId = teamId });

        Assert.Empty(log.State.Teams);
        Assert.False(log.State.Participants.ContainsKey(managerId));
        Assert.True(log.State.Participants.ContainsKey(operatorId));
    }

    [Fact]
    public void ParticipantStaged_Adds_A_Participant_With_Its_Login_Code()
    {
        var log = NewLog();
        var participantId = Ulid.NewUlid();

        log.Append(new ParticipantStaged
        {
            Id = Ulid.NewUlid(), ParticipantId = participantId, Code = "ABC123", Role = ParticipantRole.Facilitator,
            TeamId = null, DisplayName = "Ведущий",
        });

        var participant = log.State.Participants[participantId];
        Assert.Equal("ABC123", participant.Code);
        Assert.Equal(ParticipantRole.Facilitator, participant.Role);
        Assert.Equal("Ведущий", participant.DisplayName);
    }

    [Fact]
    public void ParticipantUnstaged_Removes_The_Participant()
    {
        var log = NewLog();
        var participantId = Ulid.NewUlid();
        log.Append(new ParticipantStaged
        {
            Id = Ulid.NewUlid(), ParticipantId = participantId, Code = "ABC123", Role = ParticipantRole.Operator,
            TeamId = null, DisplayName = "Оператор",
        });

        log.Append(new ParticipantUnstaged { Id = Ulid.NewUlid(), ParticipantId = participantId });

        Assert.Empty(log.State.Participants);
    }
}
