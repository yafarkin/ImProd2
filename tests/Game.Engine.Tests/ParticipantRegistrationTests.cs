using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>Регистрация участников и вход по коду (Блок 8.1, SPEC §3).</summary>
public class ParticipantRegistrationTests
{
    [Fact]
    public void RegisterParticipant_Registers_A_Manager_Bound_To_An_Existing_Team()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();

        var entry = session.RegisterParticipant(ParticipantRole.Manager, teamId, "Управляющий А1", new Random(1));

        var registered = Assert.IsType<ParticipantRegistered>(entry.Change);
        Assert.Equal(ParticipantRole.Manager, registered.Role);
        Assert.Equal(teamId, registered.TeamId);
        Assert.Equal(6, registered.Code.Length);
        Assert.True(session.State.Participants.ContainsKey(registered.Code));
    }

    [Theory]
    [InlineData(ParticipantRole.Operator)]
    [InlineData(ParticipantRole.Facilitator)]
    [InlineData(ParticipantRole.Administrator)]
    public void RegisterParticipant_Registers_A_Non_Team_Role_Without_A_Team(ParticipantRole role)
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam();

        var entry = session.RegisterParticipant(role, teamId: null, "Участник", new Random(1));

        var registered = Assert.IsType<ParticipantRegistered>(entry.Change);
        Assert.Null(registered.TeamId);
    }

    [Theory]
    [InlineData(ParticipantRole.Manager)]
    [InlineData(ParticipantRole.Negotiator)]
    public void RegisterParticipant_Throws_When_A_Team_Scoped_Role_Has_No_Team(ParticipantRole role)
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam();

        Assert.Throws<ArgumentException>(() => session.RegisterParticipant(role, teamId: null, "Участник", new Random(1)));
    }

    [Fact]
    public void RegisterParticipant_Throws_When_A_Non_Team_Role_Has_A_Team()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();

        Assert.Throws<ArgumentException>(() => session.RegisterParticipant(ParticipantRole.Operator, teamId, "Оператор", new Random(1)));
    }

    [Fact]
    public void RegisterParticipant_Throws_For_An_Unknown_Team()
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam();

        Assert.Throws<ArgumentException>(() => session.RegisterParticipant(ParticipantRole.Manager, Ulid.NewUlid(), "Участник", new Random(1)));
    }

    [Fact]
    public void RegisterParticipant_Never_Reuses_A_Code_Already_Taken()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();

        // new Random(1) с нуля даёт одну и ту же первую последовательность символов каждый раз —
        // если бы коллизия не отрабатывала, второй код совпал бы с первым.
        var first = (ParticipantRegistered)session.RegisterParticipant(ParticipantRole.Manager, teamId, "Управляющий", new Random(1)).Change;
        var second = (ParticipantRegistered)session.RegisterParticipant(ParticipantRole.Negotiator, teamId, "Переговорщик", new Random(1)).Change;

        Assert.NotEqual(first.Code, second.Code);
    }

    [Fact]
    public void TryAuthenticate_Finds_A_Registered_Participant_By_Code()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        var registered = (ParticipantRegistered)session.RegisterParticipant(ParticipantRole.Manager, teamId, "Управляющий", new Random(1)).Change;

        var found = session.TryAuthenticate(registered.Code);

        Assert.NotNull(found);
        Assert.Equal(ParticipantRole.Manager, found!.Role);
        Assert.Equal(teamId, found.TeamId);
        Assert.Equal("Управляющий", found.DisplayName);
    }

    [Fact]
    public void TryAuthenticate_Returns_Null_For_An_Unknown_Code()
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam();

        Assert.Null(session.TryAuthenticate("NOSUCH"));
    }
}
