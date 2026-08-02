namespace Game.Engine;

/// <summary>Одна строка журнала переходов сессии для экрана — реальное время события, что произошло, и ход/фаза, к которым оно привело.</summary>
public sealed record SessionHistoryRow(DateTimeOffset Timestamp, string Description, int Turn, TurnPhase Phase);

/// <summary>
/// Разворачивает журнал сессии в список «когда и в какой статус она переходила» (запрос
/// пользователя на отдельном экране управления сессией) — тем же приёмом, что и
/// <see cref="PhaseTimerCalculator"/>: выводится заново из <see cref="GameSession.Entries"/> при
/// каждом обращении, не хранится отдельно. Ход/фаза после каждого события считаются тем же
/// правилом, что и <see cref="PhaseAdvanced.Apply"/> (сознательно продублировано в малом объёме —
/// для отображения полный повтор всего состояния через реальный <see cref="GameSessionState"/>
/// был бы для журнальной таблицы избыточен).
/// </summary>
public static class SessionHistoryCalculator
{
    public static IReadOnlyList<SessionHistoryRow> Build(IReadOnlyList<EventLogEntry<GameSessionState>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var rows = new List<SessionHistoryRow>();
        var turn = 0;
        var phase = TurnPhase.Settlement;
        var endTurn = 0;

        foreach (var entry in entries)
        {
            switch (entry.Change)
            {
                case SessionStarted started:
                    turn = 1;
                    phase = TurnPhase.Settlement;
                    endTurn = started.EndTurn;
                    rows.Add(new SessionHistoryRow(entry.Timestamp, "Сессия начата", turn, phase));
                    break;

                case PhaseAdvanced advanced:
                    if (phase == TurnPhase.Decision && turn == endTurn)
                    {
                        // Тот же случай окончания игры, что и в PhaseAdvanced.Apply — ход/фаза дальше не меняются.
                        rows.Add(new SessionHistoryRow(entry.Timestamp, "Сессия завершена", turn, phase));
                        break;
                    }

                    var triggerLabel = advanced.Trigger == PhaseTransitionTrigger.Timer ? "по таймеру" : "ведущим";
                    if (phase == TurnPhase.Settlement)
                    {
                        phase = TurnPhase.Decision;
                    }
                    else
                    {
                        phase = TurnPhase.Settlement;
                        turn++;
                    }

                    rows.Add(new SessionHistoryRow(entry.Timestamp, $"Переход фазы ({triggerLabel})", turn, phase));
                    break;

                case PhaseExtended extended:
                    rows.Add(new SessionHistoryRow(
                        entry.Timestamp, $"Фаза продлена на {extended.By.TotalSeconds:0} с", turn, phase));
                    break;

                case SessionPaused:
                    rows.Add(new SessionHistoryRow(entry.Timestamp, "Пауза", turn, phase));
                    break;

                case SessionResumed:
                    rows.Add(new SessionHistoryRow(entry.Timestamp, "Возобновлено", turn, phase));
                    break;
            }
        }

        return rows;
    }
}
