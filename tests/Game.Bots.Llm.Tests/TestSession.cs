using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Bots.Llm.Tests;

/// <summary>Общий вход в реальный пилотный конфиг для тестов LLM-слоя — тот же приём, что и <c>PilotBotSession</c> в Game.Bots.Tests, но без ботов: один <see cref="GameSession"/> с одной командой.</summary>
internal static class TestSession
{
    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "Samples", "gameconfig.pilot.json");

    /// <summary>Одна команда в секторе А, достаточно долгая сессия, чтобы фаза решений точно была открыта.</summary>
    public static (GameSession Session, Ulid TeamId) StartSingleTeamSession(int endTurn = 20)
    {
        var config = GameConfigLoader.LoadFromFile(ConfigPath);
        var teamId = Ulid.NewUlid();
        var teams = new List<TeamSpec>
        {
            new() { Id = teamId, Name = "Команда", SectorId = "A" },
        };

        var session = GameSession.StartWithEndTurn(config, "short", endTurn, teams);
        // Сессия открывается в фазе расчёта (Settlement, см. SessionStarted) — решения команд
        // допустимы только в Decision, продвигаем один раз, как и обычный ход игры.
        session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
        return (session, teamId);
    }

    /// <summary>Decision → Settlement → RunTick → Decision — тот же приём, что и <c>BotSessionRunner</c>, для тестов, которым нужна реальная история по нескольким ходам.</summary>
    public static void SettleOneTurn(GameSession session, Random random)
    {
        session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
        session.RunTick(random);
        session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
    }
}
