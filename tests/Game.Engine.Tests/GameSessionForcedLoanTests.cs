namespace Game.Engine.Tests;

/// <summary>
/// Сквозная проверка через полный тик (<see cref="GameSession.RunTick"/>) того самого баг-репорта
/// пользователя: баланс мог быть неотрицательным сразу после финансового шага, но уходил в минус
/// позже, от переменных затрат на работу фабрики (энергия, известна только после расчёта
/// производства) — а принудительный заём в старой версии решался ДО этого и не замечал новую дыру.
/// Юниты самого решения — в ForcedLoanStepTests; здесь важен именно порядок событий внутри тика.
/// </summary>
public class GameSessionForcedLoanTests
{
    [Fact]
    public void RunTick_Covers_A_Shortfall_That_Only_Appears_After_Production_Overhead()
    {
        // ElectricityConsumptionPerOutputUnit=40, ElectricityBasePrice=1 (TestGameConfig) — при
        // выпуске 1 единицы (1 рабочий, ProductionRate=1, ниже BaseWorkerCount=5 — без убывающей
        // отдачи) переменная часть затрат на работу фабрики (энергия) = 1 * 40 * 1 = 40.
        var config = TestGameConfig.BuildWithFactoryUpkeep(electricityConsumptionPerOutputUnit: 40m);
        var teamId = Ulid.NewUlid();
        var log = new EventLog<GameSessionState>(new GameSessionState(config));
        var session = GameSession.StartWithEndTurn(
            log, "test", endTurn: 999,
            new[] { new TeamSpec { Id = teamId, Name = "Команда А1", SectorId = TestGameConfig.SectorA.Id } });
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision, ход 1

        session.TakeLoan(teamId, 200m);
        var built = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mine.Id).Change; // -100, баланс 100
        session.SetWorkerCount(teamId, built.FactoryId, 1); // объявление бесплатно; реальный наём и зарплата — на расчёте

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        var appended = session.RunTick(new Random(1));

        // Баланс сразу после финансового шага: 100 - проценты(200*0.05=10) - наём(1*50=50) -
        // зарплата(1*5=5) = 35 — положительно, принудительного займа тут ещё нет и по старой логике бы
        // не появилось. Только после FactoryProduced (переменные затраты -40) баланс 35-40=-5 уходит в
        // минус — и только тогда, самым последним, должен появиться принудительный заём.
        var lastFinanceSequence = appended
            .Where(e => e.Change is LoanInterestCharged or WorkersHired or SalariesPaid)
            .Max(e => e.SequenceNumber);
        var producedSequence = Assert.Single(appended, e => e.Change is FactoryProduced).SequenceNumber;
        var forcedLoanEntry = Assert.Single(appended, e => e.Change is ForcedLoanTaken);

        Assert.True(producedSequence > lastFinanceSequence); // производство после финансового шага
        Assert.True(forcedLoanEntry.SequenceNumber > producedSequence); // заём после производства, не до него — сама суть баг-репорта
        Assert.Equal(5m, ((ForcedLoanTaken)forcedLoanEntry.Change).Amount);
        Assert.Equal(0m, session.State.Teams[teamId].Balance); // заём ровно закрыл дыру — не осталось в минусе до следующего хода
        Assert.True(session.VerifyIntegrity());
    }
}
