using Game.Engine;

namespace Game.Bots;

/// <summary>
/// Прогоняет игровую сессию силами набора простых ботов (Блок 7.1) от текущего состояния до конца
/// — без какого-либо внешнего вмешательства, для автопрогонов и харнесса балансировки (Блок 7.2).
/// </summary>
public static class BotSessionRunner
{
    public static void RunToCompletion(GameSession session, IReadOnlyList<SimpleBot> bots, Random random)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(bots);
        ArgumentNullException.ThrowIfNull(random);

        var contractPairs = PairBySector(bots);
        var hasBuiltOut = false;

        while (!session.State.IsFinished)
        {
            switch (session.State.CurrentPhase)
            {
                case TurnPhase.Calculation:
                    session.RunTick(random);
                    session.AdvancePhase(PhaseTransitionTrigger.Timer);
                    break;

                case TurnPhase.Decision:
                    if (!hasBuiltOut)
                    {
                        foreach (var bot in bots)
                        {
                            bot.BuildOutSectorChain(session);
                        }
                        hasBuiltOut = true;
                    }

                    foreach (var (seller, buyer) in contractPairs)
                    {
                        SimpleBot.TrySignSimpleContract(session, seller, buyer, random);
                    }
                    foreach (var bot in bots)
                    {
                        bot.SellSurplusToSystem(session);
                    }

                    session.AdvancePhase(PhaseTransitionTrigger.Timer);
                    break;

                case TurnPhase.Closing:
                    session.AdvancePhase(PhaseTransitionTrigger.Timer);
                    break;
            }
        }
    }

    /// <summary>Внутри каждого сектора разбивает ботов на пары (продавец, покупатель) для <see cref="SimpleBot.TrySignSimpleContract"/>; лишний бот при нечётном числе — без пары.</summary>
    private static IReadOnlyList<(SimpleBot Seller, SimpleBot Buyer)> PairBySector(IReadOnlyList<SimpleBot> bots)
    {
        var pairs = new List<(SimpleBot, SimpleBot)>();
        foreach (var sectorBots in bots.GroupBy(bot => bot.Sector.Id).OrderBy(group => group.Key))
        {
            var ordered = sectorBots.OrderBy(bot => bot.TeamId).ToList();
            for (var i = 0; i + 1 < ordered.Count; i += 2)
            {
                pairs.Add((ordered[i], ordered[i + 1]));
            }
        }

        return pairs;
    }
}
