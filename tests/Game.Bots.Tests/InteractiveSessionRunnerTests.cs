using Game.Config.Loading;
using Game.Engine;

namespace Game.Bots.Tests;

/// <summary>
/// <see cref="InteractiveSessionRunner"/> — интерактивный режим <c>/admin/balance-lab</c>
/// (продолжение «отладка производства», 2026-08-24, docs/rebalance-2sector/balance-experiment-plan.md):
/// человек ведёт одну команду вручную вместо <see cref="SimpleBot"/>, остальные секторы бот по-прежнему
/// ведёт сам. Проверяет три вещи: ручные действия применяются сразу (как у настоящего игрока на
/// <c>Team.razor</c>), ошибочные действия не бросают исключение наружу, а бот соседнего сектора
/// продолжает действовать сам, пока человек занят своим.
/// </summary>
public class InteractiveSessionRunnerTests
{
    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "production-models", "control-twin-metallurgy.json");
    private static string SessionPath => Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "pilot.json");

    private static ResolvedGameConfig LoadConfig() => GameConfigLoader.LoadFromFiles(ConfigPath, SessionPath);

    [Fact]
    public void Start_Opens_The_Manual_Team_Ready_For_Decisions_On_Turn_One()
    {
        var config = LoadConfig();

        var runner = InteractiveSessionRunner.Start(config, endTurn: 5, manualSectorId: "A", maintainFactories: true, leverage: 1m, profile: 0m);

        Assert.Equal(TurnPhase.Decision, runner.Session.State.CurrentPhase);
        Assert.Equal(1, runner.Session.State.CurrentTurn);
        Assert.False(runner.Session.State.IsFinished);
        var manualTeam = runner.Session.State.Teams[runner.ManualTeamId];
        Assert.Equal("A", manualTeam.Sector.Id);
    }

    [Fact]
    public void ApplyManualAction_BuildFactory_Spends_Money_And_Adds_The_Factory_Immediately()
    {
        var config = LoadConfig();
        var runner = InteractiveSessionRunner.Start(config, endTurn: 5, manualSectorId: "A", maintainFactories: true, leverage: 1m, profile: 0m);
        var balanceBefore = runner.Session.State.Teams[runner.ManualTeamId].Balance;

        var result = runner.ApplyManualAction(new ManualAction { Kind = ManualActionKind.BuildFactory, FactoryDefinitionId = "iron-mine" });

        var success = Assert.IsType<ManualActionResult.Success>(result);
        Assert.Contains("iron-mine", success.Description);
        var manualTeam = runner.Session.State.Teams[runner.ManualTeamId];
        Assert.Single(manualTeam.Factories, f => f.Definition.Id == "iron-mine");
        Assert.True(manualTeam.Balance < balanceBefore, "Постройка обязана списать деньги немедленно, не после хода.");
    }

    [Fact]
    public void ApplyManualAction_With_Unknown_Factory_Returns_Failure_Instead_Of_Throwing()
    {
        var config = LoadConfig();
        var runner = InteractiveSessionRunner.Start(config, endTurn: 5, manualSectorId: "A", maintainFactories: true, leverage: 1m, profile: 0m);

        var result = runner.ApplyManualAction(new ManualAction { Kind = ManualActionKind.BuildFactory, FactoryDefinitionId = "does-not-exist" });

        Assert.IsType<ManualActionResult.Failure>(result);
    }

    [Fact]
    public void ApplyManualAction_Missing_Required_Field_Returns_Failure_Instead_Of_Throwing()
    {
        var config = LoadConfig();
        var runner = InteractiveSessionRunner.Start(config, endTurn: 5, manualSectorId: "A", maintainFactories: true, leverage: 1m, profile: 0m);

        // SellToSystem без materialId/volume — не должно уронить процесс, только вернуть Failure.
        var result = runner.ApplyManualAction(new ManualAction { Kind = ManualActionKind.SellToSystem });

        Assert.IsType<ManualActionResult.Failure>(result);
    }

    /// <summary>
    /// Сектор Б (не под ручным управлением) — тот же <see cref="SimpleBot"/>, что и в обычном
    /// автопрогоне: сам разворачивает первую цепочку на первом же ходу decision — регрессия на
    /// «партия не превращается в одиночный тест», см. doc-comment <see cref="InteractiveSessionRunner"/>.
    /// </summary>
    [Fact]
    public void CompleteTurn_Advances_The_Turn_While_The_Bot_Still_Builds_Out_Its_Own_Sector()
    {
        var config = LoadConfig();
        var runner = InteractiveSessionRunner.Start(config, endTurn: 5, manualSectorId: "A", maintainFactories: true, leverage: 1m, profile: 0m);

        var turnResult = runner.CompleteTurn();

        Assert.Equal(1, turnResult.Turn);
        Assert.False(turnResult.IsFinished);
        Assert.Equal(2, runner.Session.State.CurrentTurn);
        Assert.Equal(TurnPhase.Decision, runner.Session.State.CurrentPhase);
        Assert.Contains(turnResult.BotTraceLines, line => line.StartsWith("[B]", StringComparison.Ordinal));

        var botTeam = runner.Session.State.Teams.Values.Single(t => t.Sector.Id == "B");
        Assert.NotEmpty(botTeam.Factories);
    }

    /// <summary>
    /// Прямой запрос пользователя 2026-08-24: «хочу видеть предложенные действия бота и обоснование,
    /// но опционально менять их или не менять вообще» — предложение обязано ничего не трогать в
    /// настоящей сессии (баланс/фабрики не меняются просто от вызова), пока пользователь явно не
    /// применит его через <see cref="InteractiveSessionRunner.ApplyProposedActions"/>.
    /// </summary>
    [Fact]
    public void ProposeManualTeamDecision_Does_Not_Touch_The_Real_Session()
    {
        var config = LoadConfig();
        var runner = InteractiveSessionRunner.Start(config, endTurn: 5, manualSectorId: "A", maintainFactories: true, leverage: 1m, profile: 0m);
        var balanceBefore = runner.Session.State.Teams[runner.ManualTeamId].Balance;
        var factoryCountBefore = runner.Session.State.Teams[runner.ManualTeamId].Factories.Count;

        var proposal = runner.ProposeManualTeamDecision();

        Assert.NotEmpty(proposal.Actions);
        Assert.Contains(proposal.Actions, a => a.Kind == ManualActionKind.BuildFactory);
        Assert.NotEmpty(proposal.Reasoning.Build); // обоснование — тот же текст, что "строю .../...: cost=...", видимый в «Пошаговом просмотре»
        Assert.Equal(balanceBefore, runner.Session.State.Teams[runner.ManualTeamId].Balance);
        Assert.Equal(factoryCountBefore, runner.Session.State.Teams[runner.ManualTeamId].Factories.Count);
    }

    /// <summary>
    /// Регрессия на самый частый случай предложения: постройка сразу сопровождается наймом рабочих на
    /// эту же, только что построенную фабрику (см. <see cref="SimpleBot.BuildNewlyUnlockedFactories"/>)
    /// — черновой Id фабрики из предложения обязан переотобразиться на настоящий, иначе «наём» ссылался
    /// бы на фабрику, которой в настоящей сессии не существует.
    /// </summary>
    [Fact]
    public void ApplyProposedActions_Remaps_The_Draft_FactoryId_To_The_Real_One_For_A_Build_Followed_By_Worker_Count()
    {
        var config = LoadConfig();
        var runner = InteractiveSessionRunner.Start(config, endTurn: 5, manualSectorId: "A", maintainFactories: true, leverage: 1m, profile: 0m);
        var proposal = runner.ProposeManualTeamDecision();

        var buildIndex = proposal.Actions.ToList().FindIndex(a => a.Kind == ManualActionKind.BuildFactory);
        var hireIndex = proposal.Actions.ToList().FindIndex(a => a.Kind == ManualActionKind.SetWorkerCount && a.FactoryId == proposal.Actions[buildIndex].FactoryId);
        Assert.True(buildIndex >= 0, "Предложение обязано содержать хотя бы одну постройку.");
        Assert.True(hireIndex >= 0, "Постройка обязана сопровождаться наймом на ту же фабрику (см. SimpleBot.BuildNewlyUnlockedFactories).");

        var results = runner.ApplyProposedActions(proposal.Actions);

        Assert.All(results, r => Assert.IsType<ManualActionResult.Success>(r));
        var manualTeam = runner.Session.State.Teams[runner.ManualTeamId];
        Assert.NotEmpty(manualTeam.Factories);
        // DesiredWorkers, не Workers — реальный наём случится только на следующем расчёте
        // (WorkforceStep), объявление применяется немедленно (см. doc-comment GameSession.SetWorkerCount).
        Assert.Contains(manualTeam.Factories, f => f.DesiredWorkers > 0); // наём реально применился к настоящей фабрике, не потерялся
    }

    /// <summary>Пользователь снял часть галочек — только отобранные действия применяются, остальные предложение просто не трогает.</summary>
    [Fact]
    public void ApplyProposedActions_Applies_Only_The_Subset_Passed_In()
    {
        var config = LoadConfig();
        var runner = InteractiveSessionRunner.Start(config, endTurn: 5, manualSectorId: "A", maintainFactories: true, leverage: 1m, profile: 0m);
        var proposal = runner.ProposeManualTeamDecision();
        var buildActions = proposal.Actions.Where(a => a.Kind == ManualActionKind.BuildFactory).ToList();
        Assert.NotEmpty(buildActions);

        // Применяем только постройки, без сопутствующего найма — допустимый, хоть и странноватый выбор пользователя.
        var results = runner.ApplyProposedActions(buildActions);

        Assert.All(results, r => Assert.IsType<ManualActionResult.Success>(r));
        var manualTeam = runner.Session.State.Teams[runner.ManualTeamId];
        Assert.Equal(buildActions.Count, manualTeam.Factories.Count);
        Assert.All(manualTeam.Factories, f => Assert.Equal(0, f.DesiredWorkers)); // найм не применялся — не объявлено ни одного рабочего
    }

    [Fact]
    public void CompleteTurn_Snapshot_Reflects_The_Manual_Teams_Own_Action_This_Turn()
    {
        var config = LoadConfig();
        var runner = InteractiveSessionRunner.Start(config, endTurn: 5, manualSectorId: "A", maintainFactories: true, leverage: 1m, profile: 0m);

        var buildResult = Assert.IsType<ManualActionResult.Success>(
            runner.ApplyManualAction(new ManualAction { Kind = ManualActionKind.BuildFactory, FactoryDefinitionId = "iron-mine" }));
        Assert.NotNull(buildResult);

        var turnResult = runner.CompleteTurn();

        Assert.Equal(1, turnResult.Turn);
        Assert.True(turnResult.ManualTeamSnapshot.BuildCost > 0m, "Снимок хода обязан увидеть постройку, сделанную вручную в этом же ходу.");
    }
}
