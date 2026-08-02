namespace Game.Engine.Tests;

/// <summary>Журнал переходов сессии для экрана управления (запрос пользователя «когда и в какой статус сессия переходила»).</summary>
public class SessionHistoryCalculatorTests
{
    [Fact]
    public void Build_Starts_With_The_Session_Started_Row_At_Turn_One_Settlement()
    {
        var session = GameSession.StartWithEndTurn(TestGameConfig.Resolved, "short", endTurn: 10, Array.Empty<TeamSpec>());

        var rows = SessionHistoryCalculator.Build(session.Entries);

        var row = Assert.Single(rows);
        Assert.Equal("Сессия начата", row.Description);
        Assert.Equal(1, row.Turn);
        Assert.Equal(TurnPhase.Settlement, row.Phase);
    }

    [Fact]
    public void Build_Tracks_Turn_And_Phase_Through_Timer_Driven_Advances()
    {
        var session = GameSession.StartWithEndTurn(TestGameConfig.Resolved, "short", endTurn: 10, Array.Empty<TeamSpec>());

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement(1) -> Decision(1)
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision(1) -> Settlement(2)

        var rows = SessionHistoryCalculator.Build(session.Entries);

        Assert.Equal(3, rows.Count);
        Assert.Equal((1, TurnPhase.Decision), (rows[1].Turn, rows[1].Phase));
        Assert.Contains("по таймеру", rows[1].Description);
        Assert.Equal((2, TurnPhase.Settlement), (rows[2].Turn, rows[2].Phase));
    }

    [Fact]
    public void Build_Labels_A_Facilitator_Triggered_Advance_Differently_From_A_Timer_One()
    {
        var session = GameSession.StartWithEndTurn(TestGameConfig.Resolved, "short", endTurn: 10, Array.Empty<TeamSpec>());

        session.AdvancePhase(PhaseTransitionTrigger.Facilitator);

        var rows = SessionHistoryCalculator.Build(session.Entries);

        Assert.Contains("ведущим", rows[1].Description);
    }

    [Fact]
    public void Build_Records_Pause_Resume_And_Phase_Extension()
    {
        var session = GameSession.StartWithEndTurn(TestGameConfig.Resolved, "short", endTurn: 10, Array.Empty<TeamSpec>());

        session.Pause();
        session.Resume();
        session.ExtendCurrentPhase(TimeSpan.FromSeconds(30));

        var rows = SessionHistoryCalculator.Build(session.Entries);

        Assert.Equal("Пауза", rows[1].Description);
        Assert.Equal("Возобновлено", rows[2].Description);
        Assert.Equal("Фаза продлена на 30 с", rows[3].Description);
    }

    [Fact]
    public void Build_Marks_The_Transition_Out_Of_The_Last_Decision_As_The_Session_Ending()
    {
        var session = GameSession.StartWithEndTurn(TestGameConfig.Resolved, "short", endTurn: 1, Array.Empty<TeamSpec>());
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement(1) -> Decision(1)

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // конец хода 1 == EndTurn

        var rows = SessionHistoryCalculator.Build(session.Entries);

        var last = rows[^1];
        Assert.Equal("Сессия завершена", last.Description);
        Assert.Equal(1, last.Turn);
        Assert.Equal(TurnPhase.Decision, last.Phase);
    }
}
