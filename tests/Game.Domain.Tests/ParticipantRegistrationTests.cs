namespace Game.Domain.Tests;

public class ParticipantRegistrationTests
{
    [Theory]
    [InlineData(ParticipantRole.Manager)]
    [InlineData(ParticipantRole.Negotiator)]
    public void Team_Scoped_Roles_Require_A_Team(ParticipantRole role)
    {
        Assert.Throws<ArgumentException>(() => new ParticipantRegistration("ABCDEF", role, teamId: null, "Игрок"));
    }

    [Theory]
    [InlineData(ParticipantRole.Operator)]
    [InlineData(ParticipantRole.Facilitator)]
    [InlineData(ParticipantRole.Administrator)]
    public void Non_Team_Scoped_Roles_Must_Not_Have_A_Team(ParticipantRole role)
    {
        Assert.Throws<ArgumentException>(() => new ParticipantRegistration("ABCDEF", role, Ulid.NewUlid(), "Игрок"));
    }

    [Fact]
    public void A_Team_Scoped_Registration_With_A_Team_Is_Valid()
    {
        var teamId = Ulid.NewUlid();
        var registration = new ParticipantRegistration("ABCDEF", ParticipantRole.Manager, teamId, "Управляющий А1");

        Assert.Equal(teamId, registration.TeamId);
    }
}
