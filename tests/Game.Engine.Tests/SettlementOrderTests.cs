using Game.Domain;

namespace Game.Engine.Tests;

public class SettlementOrderTests
{
    private static List<Team> MakeTeams(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new Team(Ulid.NewUlid(), $"Команда {i}", TestGameConfig.SectorA))
            .OrderBy(team => team.Id)
            .ToList();

    [Fact]
    public void First_Turn_Uses_Ascending_Team_Id()
    {
        var teams = MakeTeams(4);

        Assert.Equal(teams, SettlementOrder.ForTurn(teams.AsEnumerable().Reverse(), turn: 1));
    }

    [Fact]
    public void Every_Turn_Is_A_Permutation_Of_All_Teams()
    {
        var teams = MakeTeams(5);

        for (var turn = 1; turn <= 30; turn++)
        {
            var order = SettlementOrder.ForTurn(teams, turn);
            Assert.Equal(teams.Count, order.Distinct().Count());
            Assert.True(teams.ToHashSet().SetEquals(order));
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(8)]
    public void Over_A_Full_Cycle_Each_Team_Takes_Each_Place_Twice(int count)
    {
        var teams = MakeTeams(count);
        var placesByTeam = teams.ToDictionary(team => team, _ => new int[count]);

        for (var turn = 1; turn <= 2 * count; turn++)
        {
            var order = SettlementOrder.ForTurn(teams, turn);
            for (var place = 0; place < count; place++)
            {
                placesByTeam[order[place]][place]++;
            }
        }

        Assert.All(placesByTeam.Values, places => Assert.All(places, times => Assert.Equal(2, times)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(8)]
    public void Over_A_Full_Cycle_Every_Pair_Goes_Each_Way_Half_The_Time(int count)
    {
        var teams = MakeTeams(count);
        var cycle = 2 * count;

        for (var a = 0; a < count; a++)
        {
            for (var b = a + 1; b < count; b++)
            {
                var aFirst = 0;
                for (var turn = 1; turn <= cycle; turn++)
                {
                    var order = SettlementOrder.ForTurn(teams, turn).ToList();
                    if (order.IndexOf(teams[a]) < order.IndexOf(teams[b]))
                    {
                        aFirst++;
                    }
                }

                Assert.Equal(cycle / 2, aFirst);
            }
        }
    }

    [Fact]
    public void Same_Turn_Gives_Same_Order()
    {
        var teams = MakeTeams(6);

        Assert.Equal(SettlementOrder.ForTurn(teams, 17), SettlementOrder.ForTurn(teams, 17));
    }
}
