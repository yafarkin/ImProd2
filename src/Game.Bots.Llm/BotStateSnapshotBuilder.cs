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

        var cross = GetCrossSectorMaterials(state, team);

        var text = new StringBuilder();
        AppendHeader(text, state);
        AppendTeamFinancials(text, state, team);
        AppendFactories(text, state, team);
        AppendBuildableFactoryTypes(text, state, team);
        AppendWarehouse(text, team);
        AppendMarket(text, state, team.Sector);
        AppendContracts(text, state, teamId);
        AppendCrossSectorDemand(text, state, team, cross);
        AppendTradeOffers(text, state, teamId);
        AppendActionSuggestions(text, state, teamId, team, cross);
        AppendRanking(text, session);

        return text.ToString();
    }

    /// <summary>
    /// Сознательно не показывает <see cref="GameSessionState.EndTurn"/> (запрос пользователя
    /// 2026-08-16, проверено по коду UI): реальный игрок на <c>/team</c> видит только «Ход N, фаза
    /// X» (<c>Team.razor</c>) — ни точный ход окончания, ни даже диапазон пресета нигде не
    /// показываются, конкретный `EndTurn` разыгрывается один раз при старте и остаётся тайной от
    /// команд намеренно (SPEC). Бот не должен знать больше, чем настоящий игрок за тем же столом.
    /// </summary>
    private static void AppendHeader(StringBuilder text, GameSessionState state)
    {
        text.AppendLine($"=== Turn {state.CurrentTurn}, phase {state.CurrentPhase} ===");
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
                $"overhaulRequested={(factory.OverhaulRequested ? "true" : "false")} status={status}");
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

    /// <summary>
    /// Что реально нужно/производится другим сектором — прямой запрос пользователя (2026-08-20), по
    /// следам первого прогона стадии 2 (<c>_2bot_gpt_oss_20b_2stage_v1</c>): оба бота выставили на
    /// доску заявок материал, который потребляется ТОЛЬКО внутри их же сектора (pig-iron у А,
    /// pvc-resin у Б) — ни один рецепт другого сектора его не ест, так что сделка была невозможна в
    /// принципе, а модель об этом не думала («видим общую картину, а бот — нет»). Секция считается
    /// напрямую из графа рецептов (не из production-staging.md — та же логика верна для любого числа
    /// секторов, включая стадии 3-4), без домыслов: материалы своего сектора, которые ест хоть один
    /// рецепт ЧУЖОГО сектора (кандидаты на продажу через <see cref="BotCommandKind.PostSellOffer"/> —
    /// не сброс), и материалы чужих секторов, которые ест хоть один свой рецепт (кандидаты на
    /// <see cref="BotCommandKind.PostBuyOffer"/> — на что смотреть на доске). В однoceкторной сессии
    /// (стадия 1) секция не показывается вовсе — там взаимодействовать физически не с кем.
    /// </summary>
    private static void AppendCrossSectorDemand(StringBuilder text, GameSessionState state, Team team, CrossSectorMaterials cross)
    {
        if (cross.SellCandidates.Count == 0 && cross.BuyCandidates.Count == 0 && !cross.SectorsOccupied)
        {
            return;
        }

        text.AppendLine();
        text.AppendLine("CROSS-SECTOR DEMAND (other sectors exist this session — a real trade beats dumping everything on the system market)");
        text.AppendLine(cross.SellCandidates.Count > 0
            ? "Materials YOUR sector produces that another sector's recipes actually consume — good " +
              $"postSellOffer candidates: {string.Join(", ", cross.SellCandidates.Select(m => m.Id))}"
            : "No other sector's recipe currently consumes a material your sector produces.");
        text.AppendLine(cross.BuyCandidates.Count > 0
            ? "Materials YOUR OWN recipes need that come from another sector — watch the board for " +
              $"these, or postBuyOffer for them: {string.Join(", ", cross.BuyCandidates.Select(m => m.Id))}"
            : "None of your recipes need a material from another sector.");
    }

    /// <summary>
    /// Не по каталогу конфига (<c>state.Config.Sectors</c>) — по реально занятым секторам среди
    /// команд ЭТОЙ сессии: тестовый <c>gameconfig.pilot.json</c> объявляет оба сектора A/Б даже там,
    /// где играет только сектор A (см. <c>TestSession.StartSingleTeamSession</c>), а стадия 1
    /// (<c>metallurgy.json</c>) при этом сама по себе однoceкторная. Общий источник для <see
    /// cref="AppendCrossSectorDemand"/> и <see cref="AppendActionSuggestions"/> — второй секции нужны
    /// те же списки, чтобы не искать межсекторную связь дважды по-разному.
    /// </summary>
    private readonly record struct CrossSectorMaterials(bool SectorsOccupied, IReadOnlyList<Material> SellCandidates, IReadOnlyList<Material> BuyCandidates);

    private static CrossSectorMaterials GetCrossSectorMaterials(GameSessionState state, Team team)
    {
        var occupiedSectors = state.Teams.Values.Select(t => t.Sector).Distinct().Count();
        if (occupiedSectors <= 1)
        {
            return new CrossSectorMaterials(false, [], []);
        }

        var ownRecipes = state.Config.FactoryDefinitions.Where(fd => fd.Sector == team.Sector).SelectMany(fd => fd.Recipes);
        var foreignRecipes = state.Config.FactoryDefinitions.Where(fd => fd.Sector != team.Sector).SelectMany(fd => fd.Recipes);

        var sellCandidates = foreignRecipes
            .SelectMany(recipe => recipe.DirectInputMaterials)
            .Where(material => material.Sector == team.Sector)
            .Distinct()
            .OrderBy(material => material.Id, StringComparer.Ordinal)
            .ToList();
        var buyCandidates = ownRecipes
            .SelectMany(recipe => recipe.DirectInputMaterials)
            .Where(material => material.Sector != team.Sector)
            .Distinct()
            .OrderBy(material => material.Id, StringComparer.Ordinal)
            .ToList();

        return new CrossSectorMaterials(true, sellCandidates, buyCandidates);
    }

    /// <summary>
    /// Прямая наводка на конкретное действие прямо сейчас — прямой запрос пользователя (2026-08-20):
    /// «модели простые, будем в коде им активнее подсказывать», по следам v3
    /// (<c>_2bot_gpt_oss_20b_2stage_v3</c>): Бот 1 один раз попытался закрыть чужую заявку (спутав
    /// направление), Бот 2 держал полезный для соседа материал на складе, но ни разу не выставил его
    /// заново, когда предыдущая заявка истекла. Секции CROSS-SECTOR DEMAND/PUBLIC TRADE OFFERS дают
    /// боту сырые данные и полагаются на то, что он сам сопоставит одно с другим — здесь то же
    /// сопоставление уже сделано в коде и явно названо: конкретная заявка + конкретная причина, почему
    /// она подходит именно этой команде. Не подменяет решение бота (он всё равно вправе не
    /// последовать совету), только снижает нагрузку на «сложи два факта в голове».
    /// <para>
    /// Два вида наводок, оба симметричны направлению: (1) чужая открытая заявка, которую эта команда
    /// может исполнить прямо сейчас (продаёт то, что нужно её рецептам, или покупает то, что она сама
    /// производит) — <see cref="BotCommandKind.FulfillTradeOffer"/>; (2) материал, который нужен
    /// соседнему сектору (см. <see cref="CrossSectorMaterials.SellCandidates"/>), реально лежит на
    /// складе этой команды, но заявки на его продажу от неё сейчас нет — <see
    /// cref="BotCommandKind.PostSellOffer"/>. Оба списка — не более нескольких строк на ход по
    /// конструкции (доска ограничена 3 ходами жизни заявки, склад — не бесконечный ассортимент), капа
    /// на число строк не потребовалось.
    /// </para>
    /// </summary>
    private static void AppendActionSuggestions(StringBuilder text, GameSessionState state, Ulid teamId, Team team, CrossSectorMaterials cross)
    {
        if (!cross.SectorsOccupied)
        {
            return;
        }

        var openOffers = state.TradeOffers.Values.Where(offer => offer.IsOpenOn(state.CurrentTurn)).ToList();

        var suggestions = new List<string>();

        foreach (var offer in openOffers.Where(offer => offer.TeamId != teamId))
        {
            var authorName = state.Teams.TryGetValue(offer.TeamId, out var author) ? author.Name : offer.TeamId.ToString();
            var turnsLeft = offer.ExpiresAfterTurn - state.CurrentTurn + 1;

            if (offer.Direction == TradeOfferDirection.Sell && cross.BuyCandidates.Contains(offer.Material))
            {
                suggestions.Add(
                    $"FULFILL tradeOfferId={offer.Id}: {authorName} is selling {offer.Material.Id}, which your own " +
                    "recipes need (see CROSS-SECTOR DEMAND above) — just call " +
                    $"fulfillTradeOffer(tradeOfferId=\"{offer.Id}\"), volume/unitPrice are optional, {turnsLeft} turn(s) left.");
            }
            else if (offer.Direction == TradeOfferDirection.Buy && cross.SellCandidates.Contains(offer.Material))
            {
                suggestions.Add(
                    $"FULFILL tradeOfferId={offer.Id}: {authorName} wants to buy {offer.Material.Id}, which your sector " +
                    "produces — just call " +
                    $"fulfillTradeOffer(tradeOfferId=\"{offer.Id}\"), volume/unitPrice are optional, {turnsLeft} turn(s) left.");
            }
        }

        var ownOpenSellMaterials = openOffers
            .Where(offer => offer.TeamId == teamId && offer.Direction == TradeOfferDirection.Sell)
            .Select(offer => offer.Material)
            .ToHashSet();

        foreach (var material in cross.SellCandidates)
        {
            if (ownOpenSellMaterials.Contains(material))
            {
                continue;
            }

            var stock = team.Warehouse.Stock.FirstOrDefault(s => s.Material == material);
            if (stock is null || stock.Quantity <= 0m)
            {
                continue;
            }

            suggestions.Add(
                $"POST a sell offer for {material.Id}: you have {Quantity(stock.Quantity)} in your warehouse, another " +
                "sector's recipes need it, and you don't currently have an open sell offer for it — consider postSellOffer.");
        }

        text.AppendLine();
        text.AppendLine("ACTION SUGGESTIONS (computed from your recipes, warehouse, and the board — a nudge, not a command)");
        text.AppendLine(suggestions.Count > 0 ? string.Join('\n', suggestions.Select(s => $"- {s}")) : "(none right now)");
    }

    /// <summary>
    /// Доска публичных заявок (запрос пользователя 2026-08-17) — видна всем командам, включая
    /// собственные заявки автора (помечены <c>(you)</c>), чтобы можно было решить, отзывать ли).
    /// Только реально ещё исполнимые (<see cref="TradeOffer.IsOpenOn"/>) — просроченные и уже
    /// исполненные/отозванные не занимают место в промпте.
    /// </summary>
    private static void AppendTradeOffers(StringBuilder text, GameSessionState state, Ulid teamId)
    {
        text.AppendLine();
        text.AppendLine($"PUBLIC TRADE OFFERS (each disappears after {TradeOffer.MaxAgeInTurns} turns if unfulfilled)");

        var offers = state.TradeOffers.Values
            .Where(offer => offer.IsOpenOn(state.CurrentTurn))
            .OrderBy(offer => offer.PostedTurn)
            .ThenBy(offer => offer.Id)
            .ToList();

        if (offers.Count == 0)
        {
            text.AppendLine("(none open right now)");
            return;
        }

        foreach (var offer in offers)
        {
            var authorName = state.Teams.TryGetValue(offer.TeamId, out var author) ? author.Name : offer.TeamId.ToString();
            var you = offer.TeamId == teamId ? " (you)" : string.Empty;
            var direction = offer.Direction == TradeOfferDirection.Sell ? "selling" : "buying";
            var cadence = offer.Type == ContractType.Recurring ? "every turn" : "one-off";
            var turnsLeft = offer.ExpiresAfterTurn - state.CurrentTurn + 1;

            text.AppendLine(
                $"- tradeOfferId={offer.Id} {authorName}{you} {direction} materialId={offer.Material.Id} " +
                $"volume={Quantity(offer.Volume)} ({cadence}) price={Money(offer.MinPrice)}-{Money(offer.MaxPrice)} " +
                $"turns_left={turnsLeft}");
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
