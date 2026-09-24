using Game.Domain;
using Game.Engine;

namespace Game.Bots.Tests;

/// <summary>
/// Равные по цене заявки сводятся по месту команды в <see cref="SettlementOrder"/> хода, а не по
/// <see cref="Ulid"/>: иначе одинаковые боты одного сектора (одинаковые цены) всю партию отдавали бы
/// встречный объём одной и той же команде — с меньшим идентификатором.
/// </summary>
public class OrderBookTieBreakTests
{
    [Fact]
    public void Equal_Price_Sellers_Take_Turns_Getting_The_Buyer()
    {
        var config = ShippedBotSession.LoadConfig();
        var sector = config.Sectors[0];
        var material = config.Materials.Values.OrderBy(m => m.Id, StringComparer.Ordinal).First();
        var teamIds = Enumerable.Range(0, 3).Select(_ => Ulid.NewUlid()).OrderBy(id => id).ToList();
        var teams = teamIds.Select((id, i) => new TeamSpec { Id = id, Name = $"Команда {i}", SectorId = sector.Id }).ToList();
        var session = GameSession.StartWithEndTurn(config, endTurn: 20, teams);
        var random = new Random(1);

        var sellers = teamIds.Take(2).ToList();
        var buyer = teamIds[2];
        var winsBySeller = sellers.ToDictionary(id => id, _ => 0);
        var cycle = 2 * teams.Count;

        for (var turn = 1; turn <= cycle; turn++)
        {
            session.RunTick(random);
            session.AdvancePhase(PhaseTransitionTrigger.Timer);

            var contractsBefore = session.State.Contracts.Keys.ToHashSet();
            var sellOrders = sellers
                .Select(id => new TradeOrder { TeamId = id, Material = material, Volume = 1m, LimitPrice = 10m })
                .ToList();
            var buyOrders = new List<TradeOrder> { new() { TeamId = buyer, Material = material, Volume = 1m, LimitPrice = 10m } };
            OrderBook.Match(session, sellOrders, buyOrders, random);

            var signed = session.State.Contracts.Values.Single(c => !contractsBefore.Contains(c.Id));
            winsBySeller[signed.SellerTeamId]++;

            session.AdvancePhase(PhaseTransitionTrigger.Timer);
        }

        Assert.All(winsBySeller.Values, wins => Assert.Equal(cycle / 2, wins));
    }
}
