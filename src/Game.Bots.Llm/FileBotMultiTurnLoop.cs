using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Bots.Llm;

/// <summary>
/// Мультикомандный цикл решений файлового бота (блок «три LLM-бота на трёх секторах», вариант A,
/// согласовано с пользователем 2026-10-21). Отличия от соло-цикла <c>FileBotTurnLoop</c> (тот
/// заморожен для воспроизводимости архивных прогонов и не трогается): один <see
/// cref="GameSession.RunTick"/> на все команды за ход, запросы падают в свой каталог каждой
/// команды (стем <c>{n:000}</c> тот же), ход ждёт одобрения ВСЕХ команд с общим дедлайном (на ход,
/// не на команду) и останавливается целиком по STOP-файлу в любом каталоге; первым ходом каждая
/// команда получает СВОЙ rules-текст (персона) и общий протокольный текст, каталог отфильтрован по
/// сектору команды, а рынок и доска заявок (tradeOffers/needs) видны по всем секторам —
/// межсекторальную торговлю обеспечивают это и команды <c>postSellOffer/postBuyOffer/
/// fulfillTradeOffer/emergencyPurchase</c> из уже существовавшего <see cref="BotCommandExecutor"/>.
/// Продолжение остановленного мультипрогона (resume/continue) в этот блок не входит.
/// </summary>
public static class FileBotMultiTurnLoop
{
    /// <summary>Одна команда мультипрогона: идентичность в сессии, каталог обмена и свой писатель метрик. Alias «T1..» стабилен по порядку настроек — так доска заявок читается одинаково из всех запросов.</summary>
    public sealed record TeamLink(Ulid Id, string Name, string Alias, string Directory, string SectorId, string Rules, TextWriter Metrics);

    /// <summary>
    /// Общий протокольный текст первого запроса каждой команды (механика + полный список команд,
    /// включая межкомандную доску заявок и аварийную закупку). Стратегические правила — отдельно,
    /// это поле <see cref="FileBotMultiExperiment.Settings.TeamSettings.Rules"/>: «разные боты» в
    /// мультипрогоне это разные тексты правил, не разные механики.
    /// </summary>
    internal static string Protocol(int turnLimit, int minTurns, int maxTurns) =>
        "You lead one team in a shared hall against other teams, each locked to its own sector, " +
        $"over the public {minTurns}-{maxTurns} turn game horizon; this run samples at most {turnLimit} decisions. " +
        "A full profit chain generally needs other teams' intermediate products — use the public trade board (sections tradeOffers/needs). " +
        "Display numbers rounded to two decimals. Negative cash allowed. Tick: hire (raw instant, others ramp), wear, lower-to-higher production, sales. Sales last one tick, research/staff persist. " +
        "Responses: requestId + actions array. Commands: buildFactory(factoryDefinitionId[,recipeId]), setWorkerCount(factory,count), sellToSystem(materialId,volume), " +
        "emergencyPurchase(materialId,volume) (executes next turn at quote x multiplier - expensive last resort), setGenerationResearchCommitment(amount), setOverhaulRequested(factory,enabled), " +
        "postNeed(direction,materialId,volumeOrder[,comment]) direction=surplus|deficit volumeOrder=small|medium|large, withdrawNeed(needId), " +
        "postSellOffer(materialId,volume,minPrice,maxPrice[,recurring]) / postBuyOffer(materialId,volume,minPrice,maxPrice[,recurring]) - a public offer on the inter-team board that any other team may fulfill; " +
        "fulfillTradeOffer(tradeOfferId[,volume][,unitPrice]) executes another team's board offer within its price range (sell offer: you buy from the posting team; buy offer: you sell to it), withdrawTradeOffer(tradeOfferId). " +
        "Board offers expire 3 turns after posting. factory=f1 etc; new:TYPE targets latest factory of that type built earlier in this response. No automatic hiring. Empty actions allowed. No reasoning text needed.";

    public static async Task<(int Completed, string StopReason)> RunDecisionLoopAsync(
        GameSession session, ResolvedGameConfig config, string basePath,
        FileBotMultiExperiment.Settings settings, IReadOnlyDictionary<string, decimal> costs,
        IReadOnlyList<TeamLink> teams, int firstTurn, int lastTurn)
    {
        var random = new Random(settings.Seed);
        var wire = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var completed = 0;
        var previous = new List<string>[teams.Count];
        for (var i = 0; i < previous.Length; i++) previous[i] = [];
        for (var n = firstTurn; n <= lastTurn; n++)
        {
            session.RunTick(random);
            session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
            var turn = session.State.CurrentTurn;
            var board = Board(session, teams, turn);
            var requestId = $"turn-{n}";
            var stems = new string[teams.Count];
            for (var i = 0; i < teams.Count; i++)
            {
                var link = teams[i];
                var team = session.State.Teams[link.Id];
                var factories = team.Factories.OrderBy(f => f.Id).ToArray();
                var sectorFactoryIds = config.Raw.FactoryDefinitions
                    .Where(d => d.SectorId == link.SectorId).Select(d => d.Id).ToHashSet();
                static decimal R(decimal value) => decimal.Round(value, 2);
                var request = new
                {
                    requestId, turn,
                    team = new { alias = link.Alias, name = link.Name, sector = link.SectorId },
                    balance = R(team.Balance), generation = team.UnlockedGeneration,
                    research = new { invested = team.GenerationResearchInvestment, perTurn = team.GenerationResearchCommitmentPerTurn,
                        cap = config.Raw.GenerationResearch.MaxCommitmentPerTurn, points = GenerationResearchCalculator.CalculateResearchPoints(team.GenerationResearchInvestment, config.Raw.GenerationResearch) },
                    rules = n == 1 && !string.IsNullOrWhiteSpace(link.Rules) ? link.Rules : null,
                    protocol = n == 1 ? Protocol(settings.TurnLimit, config.Raw.Duration.MinTurns, config.Raw.Duration.MaxTurns) : null,
                    economy = n == 1 ? new { config.Raw.WorkerProductivity, config.Raw.GenerationResearch, config.Raw.Warehouse, config.Raw.Wear } : null,
                    catalogue = n == 1 ? config.FactoryDefinitions
                        .Where(d => sectorFactoryIds.Contains(d.Id))
                        .OrderBy(d => d.Recipes[0].Output.Level).ThenBy(d => d.Id, StringComparer.Ordinal).Select(d => new object[]
                        {d.Id, d.Recipes[0].Output.Level, config.Raw.FactoryDefinitions.Single(x => x.Id == d.Id).BuildCost,
                            config.Raw.FactoryDefinitions.Single(x => x.Id == d.Id).FixedCostPerTurn,
                            d.Recipes.Select(r => new { output = r.Output.Id, rate = r.ProductionRate, inputs = r.Inputs.Select(i => new object[]{i.Material.Id, i.Quantity/r.OutputQuantity}).ToArray() }).ToArray() }).ToArray() : null,
                    teamsOverview = n == 1 ? teams.Select(t => new { alias = t.Alias, name = t.Name, sector = t.SectorId }).ToArray() : null,
                    columns = n == 1 ? "catalogue=[type,requiredGeneration,buildCost,fixedCostPerTurn,recipes]; factories=[alias,type,workers,desired,condition,repairTurns]; market=[material,price,capacity]; stock=[material,quantity]; tradeOffers=[id,owner,side,material,type,volume,minPrice,maxPrice]; needs=[id,owner,direction,material,volumeOrder,comment]." : null,
                    factories = factories.Select((f, k) => new object[]{$"f{k + 1}", f.Definition.Id, f.Workers, f.DesiredWorkers, R(f.Condition), f.RepairTurnsRemaining}).ToArray(),
                    stock = team.Warehouse.Stock.Where(s => s.Quantity > 0).OrderBy(s => s.Material.Id, StringComparer.Ordinal).Select(s => new object[]{s.Material.Id, R(s.Quantity)}).ToArray(),
                    market = config.Materials.Values.OrderBy(m => m.Id, StringComparer.Ordinal).Select(m => new object[]{m.Id, R(session.State.Market.QuoteOf(m.Id).Price), R(session.State.Market.QuoteOf(m.Id).Capacity)}).ToArray(),
                    tradeOffers = board.Offers,
                    needs = board.Needs,
                    previousResults = previous[i].ToArray(),
                };
                stems[i] = Path.Combine(link.Directory, $"{n:000}");
                await File.WriteAllTextAsync(stems[i] + ".request.json", JsonSerializer.Serialize(request, wire));
            }
            Console.WriteLine($"WAIT {n:000} ({teams.Count} teams)");
            var deadline = DateTimeOffset.UtcNow.AddSeconds(settings.WaitSeconds);
            while (true)
            {
                if (HasStopFile(basePath, teams)) return (completed, $"stopped:stop-file:{turn}");
                var allApproved = true;
                foreach (var stem in stems)
                {
                    if (!File.Exists(stem + ".approved")) { allApproved = false; break; }
                }
                if (allApproved) break;
                if (DateTimeOffset.UtcNow >= deadline) return (completed, $"stopped:waiting-approval:{turn}");
                await Task.Delay(200);
            }
            for (var i = 0; i < teams.Count; i++)
            {
                var link = teams[i];
                var team = session.State.Teams[link.Id];
                var factories = team.Factories.OrderBy(f => f.Id).ToArray();
                var aliases = factories.Select((f, k) => (Alias: $"f{k + 1}", f.Id)).ToDictionary(x => x.Alias, x => x.Id);
                var stem = stems[i];
                var response = await File.ReadAllTextAsync(stem + ".response.json");
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(response)));
                if ((await File.ReadAllTextAsync(stem + ".approved")).Trim() != hash) throw new InvalidOperationException("Unapproved response bytes.");
                previous[i] = FileBotProtocol.Execute(response, requestId, session, link.Id, aliases, random, settings.MaxActions).ToList();
                await link.Metrics.WriteLineAsync(JsonSerializer.Serialize(new { turn = session.State.CurrentTurn, team.Balance,
                    score = FinalScoreCalculator.Calculate(team, costs, config.Raw.FactoryDefinitions), factories = team.Factories.Count, team.UnlockedGeneration, results = previous[i] }));
                Console.WriteLine($"DONE {n:000} [{link.Alias}]: {string.Join(';', previous[i])}");
            }
            completed++;
            if (n < lastTurn) session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
        }
        return (completed, "turn-limit");
    }

    private static bool HasStopFile(string basePath, IReadOnlyList<TeamLink> teams)
    {
        if (File.Exists(Path.Combine(basePath, "STOP"))) return true;
        foreach (var team in teams)
        {
            if (File.Exists(Path.Combine(team.Directory, "STOP"))) return true;
        }
        return false;
    }

    /// <summary>Доска заявок на текущий ход: открытые заявки (<see cref="TradeOffer.IsOpenOn"/>) и активные записи потребностей; владелец — альясом T1.. по порядку настроек, детерминированная сортировка.</summary>
    private static (object[] Offers, object[] Needs) Board(GameSession session, IReadOnlyList<TeamLink> teams, int turn)
    {
        static decimal R(decimal value) => decimal.Round(value, 2);
        var aliasByTeamId = teams.ToDictionary(t => t.Id, t => t.Alias);
        var offers = session.State.TradeOffers.Values
            .Where(o => o.IsOpenOn(turn))
            .OrderBy(o => o.PostedTurn).ThenBy(o => o.Id.ToString(), StringComparer.Ordinal)
            .Select(o => new object[]
            {
                o.Id.ToString(), aliasByTeamId[o.TeamId],
                o.Direction == TradeOfferDirection.Sell ? "sell" : "buy",
                o.Material.Id, o.Type == ContractType.Recurring ? "recurring" : "spot",
                R(o.Volume), R(o.MinPrice), R(o.MaxPrice),
            })
            .ToArray();
        var needs = session.State.Needs.Values
            .Where(np => np.Status == NeedStatus.Active)
            .OrderBy(np => np.Id.ToString(), StringComparer.Ordinal)
            .Select(np => new object[]
            {
                np.Id.ToString(), aliasByTeamId[np.TeamId],
                np.Direction == NeedDirection.Surplus ? "surplus" : "deficit",
                np.Material.Id,
                np.VolumeOrder == NeedVolumeOrder.Small ? "small" : np.VolumeOrder == NeedVolumeOrder.Medium ? "medium" : "large",
                np.Comment ?? "",
            })
            .ToArray();
        return (offers, needs);
    }
}
