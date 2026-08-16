using System.Globalization;
using System.Text;
using Game.Domain;
using Game.Engine;

namespace Game.Bots.Llm;

/// <summary>
/// Строит текстовый срез состояния сессии для одной команды — динамическая часть user-промпта
/// LLM-бота (шаг 4 плана, docs/TODO.md #20), пересобирается заново на каждый ход. Читает то же
/// самое состояние, что видит человек в Game.Web (Team.razor/BigScreen.razor), но напрямую из
/// <see cref="GameSessionState"/> — единого агрегирующего ViewModel-слоя в проекте нет, и заводить
/// его специально под LLM-бота избыточно, раз готовые данные и так лежат в домене.
/// Простой текст, не JSON, — дешевле по токенам для того же смысла и не зависит от того, умеет ли
/// модель "читать" JSON лучше, чем прозу (структурная форма ответа и так гарантируется отдельной
/// JSON-схемой, см. <see cref="BotCommandSchema"/> — это про другое).
/// </summary>
public static class BotStateSnapshotBuilder
{
    /// <summary>Строит срез состояния для команды <paramref name="teamId"/> по состоянию сессии на момент вызова.</summary>
    public static string Build(GameSession session, Ulid teamId)
    {
        ArgumentNullException.ThrowIfNull(session);

        var state = session.State;
        if (!state.Teams.TryGetValue(teamId, out var team))
        {
            throw new ArgumentException($"Unknown team '{teamId}'.", nameof(teamId));
        }

        var text = new StringBuilder();
        AppendHeader(text, state);
        AppendTeamFinancials(text, state, team);
        AppendFactories(text, state, team);
        AppendBuildableFactoryTypes(text, state, team);
        AppendWarehouse(text, team);
        AppendMarket(text, state, team.Sector);
        AppendContracts(text, state, teamId);
        AppendRanking(text, session);

        return text.ToString();
    }

    private static void AppendHeader(StringBuilder text, GameSessionState state)
    {
        text.AppendLine($"=== Turn {state.CurrentTurn} of {state.EndTurn}, phase {state.CurrentPhase} ===");
    }

    private static void AppendTeamFinancials(StringBuilder text, GameSessionState state, Team team)
    {
        var netWorth = team.Balance - team.Debt;
        text.AppendLine();
        text.AppendLine($"YOUR TEAM (sector {team.Sector.Id})");
        text.AppendLine($"Balance: {Money(team.Balance)} | Debt: {Money(team.Debt)} | Net worth: {Money(netWorth)}");
        text.AppendLine($"Unlocked generation: {team.UnlockedGeneration} | " +
            $"Generation research: {Money(team.GenerationResearchCommitmentPerTurn)}/turn " +
            $"(max {Money(state.Config.Raw.GenerationResearch.MaxCommitmentPerTurn)})");
    }

    private static void AppendFactories(StringBuilder text, GameSessionState state, Team team)
    {
        text.AppendLine();
        text.AppendLine("YOUR FACTORIES");
        if (team.Factories.Count == 0)
        {
            text.AppendLine("(none yet)");
            return;
        }

        var maxRnd = state.Config.Raw.Rnd.MaxCommitmentPerTurn;
        foreach (var factory in team.Factories)
        {
            var status = factory.IsUnderRepair
                ? $"under repair, {factory.RepairTurnsRemaining} turn(s) left"
                : "operating";

            text.AppendLine($"- factoryId={factory.Id} type={factory.Definition.Id} level={factory.Level} " +
                $"workers={factory.Workers}/{factory.DesiredWorkers} condition={Percent(factory.Condition)} " +
                $"recipe={factory.SelectedRecipe.Id} rnd={Money(factory.RndCommitmentPerTurn)}/turn(max {Money(maxRnd)}) " +
                $"status={status}");
        }
    }

    /// <summary>
    /// Каталог типов фабрик сектора команды с точными <c>factoryDefinitionId</c> для
    /// <see cref="BotCommandKind.BuildFactory"/> — без этой секции модель может лишь угадывать id по
    /// единственному примеру в системном промпте (живая проверка 2026-08-16: reasoning-модель
    /// однажды придумала "IronMine" вместо настоящего "iron-mine" — доменная ошибка, ретрай отработал
    /// штатно, но команда не выполнилась с первой попытки просто из-за нехватки этих данных).
    /// <para>
    /// Ограничено поколением команды +1 (живая проверка на реальном конфиге стадии 1, 26 типов
    /// фабрик в одном секторе, а не 5 как в тестовом пилотном конфиге, 2026-08-16): без этого
    /// ограничения список всех типов сразу переполнил контекст-окно модели через несколько ходов
    /// (HTTP 400 от LM Studio, обвалил весь прогон) — команда всё равно не может построить фабрику
    /// поколения выше своего +1 (<see cref="Game.Engine.GameSession.BuildFactory"/> отклонит), так
    /// что дальние поколения бесполезны в промпте прямо сейчас, покажутся, когда откроются.
    /// </para>
    /// </summary>
    private static void AppendBuildableFactoryTypes(StringBuilder text, GameSessionState state, Team team)
    {
        text.AppendLine();
        text.AppendLine($"FACTORY TYPES IN YOUR SECTOR ({team.Sector.Id}) — exact factoryDefinitionId to use with buildFactory");

        var allDefinitions = state.Config.FactoryDefinitions
            .Where(definition => definition.Sector == team.Sector)
            .OrderBy(definition => definition.Id, StringComparer.Ordinal)
            .ToList();

        var visibleMaxGeneration = team.UnlockedGeneration + 1;
        var definitions = allDefinitions
            .Where(definition => definition.Recipes[0].Output.Level <= visibleMaxGeneration)
            .ToList();
        var hiddenCount = allDefinitions.Count - definitions.Count;

        if (definitions.Count == 0)
        {
            text.AppendLine("(none)");
            return;
        }

        foreach (var definition in definitions)
        {
            var generation = definition.Recipes[0].Output.Level;
            var unlocked = generation <= team.UnlockedGeneration;
            var buildCost = state.Config.Raw.FactoryDefinitions.First(f => f.Id == definition.Id).BuildCost;
            var recipeIds = string.Join(", ", definition.Recipes.Select(recipe => recipe.Id));
            var status = unlocked ? "unlocked" : $"locked, needs generation {generation}";

            text.AppendLine($"- factoryDefinitionId={definition.Id} name={definition.Name} buildCost={Money(buildCost)} " +
                $"recipes=[{recipeIds}] status={status}");
        }

        if (hiddenCount > 0)
        {
            text.AppendLine($"(+{hiddenCount} more factory type(s) of generation {visibleMaxGeneration + 1}+, not shown — unlock generation {visibleMaxGeneration} first)");
        }
    }

    private static void AppendWarehouse(StringBuilder text, Team team)
    {
        text.AppendLine();
        text.AppendLine("WAREHOUSE");
        if (team.Warehouse.Stock.Count == 0)
        {
            text.AppendLine("(empty)");
            return;
        }

        foreach (var stock in team.Warehouse.Stock)
        {
            text.AppendLine($"- materialId={stock.Material.Id} quantity={Quantity(stock.Quantity)} avg_cost={Money(stock.AverageUnitCost)}");
        }
    }

    private static void AppendMarket(StringBuilder text, GameSessionState state, Sector sector)
    {
        text.AppendLine();
        text.AppendLine($"MARKET (your sector, electricity price {Money(state.Market.ElectricityPrice)})");

        var sectorMaterials = state.Config.Materials.Values
            .Where(material => material.Sector == sector)
            .OrderBy(material => material.Id, StringComparer.Ordinal);

        var any = false;
        foreach (var material in sectorMaterials)
        {
            if (!state.Market.HasQuote(material.Id))
            {
                continue;
            }

            any = true;
            var quote = state.Market.QuoteOf(material.Id);
            text.AppendLine($"- materialId={material.Id} price={Money(quote.Price)} capacity={Quantity(quote.Capacity)} " +
                $"sold_this_turn={Quantity(state.Market.SoldThisTurn(material.Id))}");
        }

        if (!any)
        {
            text.AppendLine("(no quotes yet)");
        }
    }

    private static void AppendContracts(StringBuilder text, GameSessionState state, Ulid teamId)
    {
        text.AppendLine();
        text.AppendLine("CONTRACTS INVOLVING YOU");

        var contracts = state.Contracts.Values
            .Where(contract => contract.BuyerTeamId == teamId || contract.SellerTeamId == teamId)
            .OrderBy(contract => contract.Id)
            .ToList();

        if (contracts.Count == 0)
        {
            text.AppendLine("(none)");
            return;
        }

        foreach (var contract in contracts)
        {
            var role = contract.BuyerTeamId == teamId ? "buyer" : "seller";
            var counterpartyId = contract.BuyerTeamId == teamId ? contract.SellerTeamId : contract.BuyerTeamId;
            var counterpartyName = state.Teams.TryGetValue(counterpartyId, out var counterparty) ? counterparty.Name : counterpartyId.ToString();

            text.AppendLine($"- contractId={contract.Id} status={contract.Status} you={role} counterparty={counterpartyName} " +
                $"materialId={contract.Terms.Material.Id} volume={Quantity(contract.Terms.Volume)} " +
                $"unit_price={Money(contract.Terms.UnitPrice)}");
        }
    }

    private static void AppendRanking(StringBuilder text, GameSession session)
    {
        text.AppendLine();
        text.AppendLine("TEAM RANKING (net worth = balance - debt)");

        var ranked = session.State.Teams.Values
            .Select(team => (team.Name, team.Sector.Id, NetWorth: team.Balance - team.Debt, Reputation: session.GetReputation(team.Id)))
            .OrderByDescending(row => row.NetWorth)
            .ToList();

        var place = 1;
        foreach (var row in ranked)
        {
            text.AppendLine($"{place}. {row.Name} (sector {row.Id}): net worth {Money(row.NetWorth)}, " +
                $"reputation {Percent(row.Reputation.Percentage / 100m)} ({row.Reputation.SampleCount} samples)");
            place++;
        }
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Quantity(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Percent(decimal fraction) => (fraction * 100m).ToString("0", CultureInfo.InvariantCulture) + "%";
}
