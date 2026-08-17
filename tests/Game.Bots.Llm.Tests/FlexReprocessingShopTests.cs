using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Bots.Llm.Tests;

/// <summary>
/// Сквозная проверка на РЕАЛЬНОМ <c>metallurgy.json</c> (не игрушечном <c>gameconfig.pilot.json</c>,
/// см. <see cref="TestSession"/>) для <c>flex-reprocessing-shop</c> — единственной многорецептной
/// фабрики стадии 1 (запрос пользователя, docs/TODO.md #20, 2026-08-17: доработать стадию 1 под выбор
/// рецепта). Ловит опечатки в id рецепта/материала, которых юнит-тесты на игрушечных фикстурах не
/// увидят, и служит дешёвой страховкой перед живым многочасовым прогоном против LM Studio — то же
/// разделение труда, что и с <c>ZZZTempParseCheck</c>-подобными временными тестами, но постоянное,
/// не разовое.
/// </summary>
public sealed class FlexReprocessingShopTests
{
    private static readonly BotCommandExecutor Executor = new();

    private static (GameSession Session, Ulid TeamId) StartSession()
    {
        var productionModelPath = Path.Combine(AppContext.BaseDirectory, "Samples", "production-models", "metallurgy.json");
        var sessionPath = Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "pilot.json");
        var config = GameConfigLoader.LoadFromFiles(productionModelPath, sessionPath);

        var teamId = Ulid.NewUlid();
        var teams = new List<TeamSpec> { new() { Id = teamId, Name = "Команда", SectorId = "A" } };
        var session = GameSession.StartWithEndTurn(config, "full", 90, teams);
        session.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement(1) -> Decision(1)
        return (session, teamId);
    }

    [Theory]
    [InlineData("scrap-alloy-from-scrap")]
    [InlineData("coal-briquette-from-coal")]
    public void BuildFactory_WithExplicitRecipeId_BuildsTheChosenVariant(string recipeId)
    {
        var (session, teamId) = StartSession();
        var command = new BotCommand
        {
            Kind = BotCommandKind.BuildFactory,
            FactoryDefinitionId = "flex-reprocessing-shop",
            RecipeId = recipeId,
        };

        var result = Executor.Execute(command, session, teamId);

        Assert.IsType<BotCommandExecutionResult.Success>(result);
        var factory = Assert.Single(session.State.Teams[teamId].Factories);
        Assert.Equal("flex-reprocessing-shop", factory.Definition.Id);
        Assert.Equal(recipeId, factory.SelectedRecipe.Id);
    }

    [Fact]
    public void BuildFactory_WithoutRecipeId_DefaultsToFirstRecipe()
    {
        // Тот же приём, что у любой другой однорецептной фабрики (Factory.cs: selectedRecipe ??=
        // definition.Recipes[0]) — модель может не указать recipeId вовсе, это не ошибка.
        var (session, teamId) = StartSession();
        var command = new BotCommand { Kind = BotCommandKind.BuildFactory, FactoryDefinitionId = "flex-reprocessing-shop" };

        var result = Executor.Execute(command, session, teamId);

        Assert.IsType<BotCommandExecutionResult.Success>(result);
        Assert.Equal("scrap-alloy-from-scrap", session.State.Teams[teamId].Factories[0].SelectedRecipe.Id);
    }

    [Fact]
    public void SelectRecipe_SwitchesAnAlreadyBuiltFactoryToTheOtherRecipe()
    {
        var (session, teamId) = StartSession();
        Executor.Execute(
            new BotCommand { Kind = BotCommandKind.BuildFactory, FactoryDefinitionId = "flex-reprocessing-shop", RecipeId = "scrap-alloy-from-scrap" },
            session, teamId);
        var factoryId = session.State.Teams[teamId].Factories[0].Id;

        var result = Executor.Execute(
            new BotCommand { Kind = BotCommandKind.SelectRecipe, FactoryId = factoryId, RecipeId = "coal-briquette-from-coal" },
            session, teamId);

        Assert.IsType<BotCommandExecutionResult.Success>(result);
        Assert.Equal("coal-briquette-from-coal", session.State.Teams[teamId].Factories[0].SelectedRecipe.Id);
    }

    [Fact]
    public void StateSnapshot_ListsBothRecipesForTheFlexShop()
    {
        // BotStateSnapshotBuilder перечисляет ВСЕ рецепты типа в каталоге (не только Recipes[0]) —
        // проверяем это против настоящего конфига, не только по коду (тот факт, что метод технически
        // join'ит весь Recipes, ещё не значит, что для ЭТОЙ фабрики они действительно оба видны модели,
        // например из-за фильтра по поколению, см. doc-comment BotStateSnapshotBuilder).
        var (session, teamId) = StartSession();

        var snapshot = BotStateSnapshotBuilder.Build(session, teamId);

        Assert.Contains("flex-reprocessing-shop", snapshot);
        Assert.Contains("scrap-alloy-from-scrap", snapshot);
        Assert.Contains("coal-briquette-from-coal", snapshot);
    }
}
