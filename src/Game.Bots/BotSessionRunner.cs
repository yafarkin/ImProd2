using Game.Engine;

namespace Game.Bots;

/// <summary>
/// Прогоняет игровую сессию силами набора простых ботов (Блок 7.1) от текущего состояния до конца
/// — без какого-либо внешнего вмешательства, для автопрогонов и харнесса балансировки (Блок 7.2).
/// Обмен материалами между ботами (в том числе между разными секторами, Блок 7.3.1) идёт через
/// упрощённый биржевой стакан (<see cref="OrderBook"/>), не через жёстко заданные пары — не привязан
/// к числу секторов конфига.
/// </summary>
public static class BotSessionRunner
{
    /// <summary>
    /// <paramref name="onTurnCompleted"/> — необязательный колбэк для харнесса балансировки (Блок
    /// 7.2): получает все события хода — и тика (финансы/производство/контракты/рынок/новости), и
    /// решений ботов, принятых по его итогам (постройка/наём/контракты/продажа), — сразу после
    /// того, как решения этого хода приняты, но ещё до перехода к следующему ходу.
    /// </summary>
    public static void RunToCompletion(
        GameSession session,
        IReadOnlyList<SimpleBot> bots,
        Random random,
        Action<IReadOnlyList<EventLogEntry<GameSessionState>>>? onTurnCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(bots);
        ArgumentNullException.ThrowIfNull(random);

        var hasBuiltOut = false;
        var turnStartIndex = session.Entries.Count;

        while (!session.State.IsFinished)
        {
            switch (session.State.CurrentPhase)
            {
                case TurnPhase.Settlement:
                    turnStartIndex = session.Entries.Count;
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

                    foreach (var bot in bots)
                    {
                        // Идемпотентно: на ходу первой постройки ничего нового не найдёт (уже
                        // построено BuildOutSectorChain), на последующих — достраивает то, что
                        // разблокировало исследование поколений.
                        bot.BuildNewlyUnlockedFactories(session);
                        bot.MaintainFactories(session);
                        bot.RepayDebt(session);
                    }

                    var sellOrders = bots.SelectMany(bot => bot.ComputeSellOrders(session)).ToList();
                    var buyOrders = bots.SelectMany(bot => bot.ComputeBuyOrders(session)).ToList();
                    OrderBook.Match(session, sellOrders, buyOrders, random);

                    foreach (var bot in bots)
                    {
                        bot.SellSurplusToSystem(session);
                    }

                    onTurnCompleted?.Invoke(session.Entries.Skip(turnStartIndex).ToList());
                    session.AdvancePhase(PhaseTransitionTrigger.Timer);
                    break;
            }
        }
    }
}
