using Game.Config.Session;

namespace Game.Engine.Tests;

/// <summary>
/// Сквозная проверка R&amp;D через полный тик (Блок 9.2, запрос пользователя: постоянные затраты,
/// растянутые по ходам, а не мгновенный скачок на несколько уровней за один большой платёж) —
/// объявление (<see cref="GameSession.SetRndCommitment"/>) и автоматическое списание
/// (<see cref="TickFinanceStep"/>, покрыто отдельно в TickFinanceStepRndTests) вместе, как их видит
/// игрок.
/// </summary>
public class GameSessionRndProgressionTests
{
    [Fact]
    public void RunTick_Advances_The_Level_Gradually_Across_Several_Turns_Below_The_Per_Turn_Cap()
    {
        // TestGameConfig.Resolved.Raw.Rnd: пороги { 100m, 300m } — 1->2, 2->3.
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision, ход 1
        session.TakeLoan(teamId, 1000m); // с запасом на все ходы ниже
        var built = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mine.Id).Change;
        session.SetRndCommitment(teamId, built.FactoryId, 50m); // ниже порога (100) — одного хода не хватит

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        session.RunTick(new Random(1));
        Assert.Equal(1, session.State.Teams[teamId].Factories.Single().Level); // 50 < 100 — ещё не перешагнули порог

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 3
        var appended = session.RunTick(new Random(1)); // накопленное 50 + 50 = 100 — ровно порог

        Assert.Equal(2, session.State.Teams[teamId].Factories.Single().Level);
        Assert.Contains(appended, e => e.Change is FactoryLevelAdvanced);
    }

    [Fact]
    public void RunTick_Applies_A_Level_Gained_This_Turn_To_This_Same_Turns_Production()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision, ход 1
        session.TakeLoan(teamId, 1000m);
        var built = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mine.Id).Change;
        session.HireWorkers(teamId, built.FactoryId, TestGameConfig.Resolved.Raw.WorkerProductivity.BaseWorkerCount); // 5, линейная отдача без убывания
        session.SetRndCommitment(teamId, built.FactoryId, 100m); // ровно первый порог — перешагнём за один ход

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        var appended = session.RunTick(new Random(1));

        Assert.Contains(appended, e => e.Change is FactoryLevelAdvanced);
        Assert.Equal(2, session.State.Teams[teamId].Factories.Single().Level);
        var produced = Assert.IsType<FactoryProduced>(appended.Single(e => e.Change is FactoryProduced).Change);
        // Рудник без сырьевых входов: recipeRate(1) * levelBonus(1 + 0.1) * workers(5) = 5.5 — уже
        // с бонусом уровня 2, полученного в этот же ход (не только со следующего).
        Assert.Equal(5.5m, produced.OutputQuantity);
    }
}
